import 'dart:convert';

import 'package:sqflite/sqflite.dart';

import '../capture/pending_command.dart';

class LocalDatabase {
  Database? _database;

  Future<Database> get _db async => _database ??= await openDatabase(
    '${await getDatabasesPath()}/unitatlas_capture.db',
    version: 1,
    onCreate: (db, _) async {
      await db.execute(
        'CREATE TABLE local_units (id TEXT PRIMARY KEY, json TEXT NOT NULL)',
      );
      await db.execute(
        'CREATE TABLE local_logistic_units (id TEXT PRIMARY KEY, json TEXT NOT NULL)',
      );
      await db.execute('''CREATE TABLE pending_commands (
        id TEXT PRIMARY KEY,
        device_id TEXT NOT NULL,
        command_type TEXT NOT NULL,
        payload_json TEXT NOT NULL,
        created_at TEXT NOT NULL,
        sync_status TEXT NOT NULL,
        attempt_count INTEGER NOT NULL,
        last_error TEXT
      )''');
      await db.execute(
        'CREATE TABLE sync_checkpoint (id INTEGER PRIMARY KEY CHECK (id = 1), token TEXT NOT NULL)',
      );
      await db.execute(
        'CREATE TABLE cached_locations (id TEXT PRIMARY KEY, json TEXT NOT NULL)',
      );
      await db.execute(
        'CREATE TABLE cached_products (id TEXT PRIMARY KEY, json TEXT NOT NULL)',
      );
    },
  );

  Future<void> cacheBootstrap(Map<String, dynamic> bootstrap) async {
    final db = await _db;
    await db.transaction((tx) async {
      await tx.delete('cached_locations');
      await tx.delete('cached_products');
      for (final row in bootstrap['locations'] as List<dynamic>? ?? []) {
        final value = row as Map<String, dynamic>;
        await tx.insert('cached_locations', {
          'id': value['id'],
          'json': jsonEncode(value),
        });
      }
      for (final row in bootstrap['products'] as List<dynamic>? ?? []) {
        final value = row as Map<String, dynamic>;
        await tx.insert('cached_products', {
          'id': value['id'],
          'json': jsonEncode(value),
        });
      }
      await tx.insert('sync_checkpoint', {
        'id': 1,
        'token': bootstrap['syncToken'] as String? ?? '0',
      }, conflictAlgorithm: ConflictAlgorithm.ignore);
    });
  }

  Future<String> checkpoint() async {
    final rows = await (await _db).query('sync_checkpoint', limit: 1);
    return rows.isEmpty ? '0' : rows.first['token']! as String;
  }

  Future<void> applyChanges(Map<String, dynamic> response) async {
    final db = await _db;
    await db.transaction((tx) async {
      for (final row in response['changes'] as List<dynamic>? ?? []) {
        final change = row as Map<String, dynamic>;
        final type = change['resourceType'] as String?;
        final table = type == 'UNIT'
            ? 'local_units'
            : type == 'LOGISTIC_UNIT'
            ? 'local_logistic_units'
            : null;
        if (table != null) {
          await tx.insert(table, {
            'id': change['resourceId'],
            'json': jsonEncode(change),
          }, conflictAlgorithm: ConflictAlgorithm.replace);
        }
      }
      await tx.insert('sync_checkpoint', {
        'id': 1,
        'token': response['nextToken'] as String? ?? '0',
      }, conflictAlgorithm: ConflictAlgorithm.replace);
    });
  }

  Future<void> enqueue(PendingCommand command) async =>
      (await _db).insert('pending_commands', command.toDatabase());

  Future<List<PendingCommand>> pending() async => (await _db)
      .query(
        'pending_commands',
        where: "sync_status IN ('PENDING', 'RETRY')",
        orderBy: 'created_at, id',
      )
      .then((rows) => rows.map(PendingCommand.fromDatabase).toList());

  Future<List<PendingCommand>> allCommands() async => (await _db)
      .query('pending_commands', orderBy: 'created_at DESC, id DESC')
      .then((rows) => rows.map(PendingCommand.fromDatabase).toList());

  Future<void> updateResult(String id, String status, String? error) async {
    final db = await _db;
    await db.rawUpdate(
      'UPDATE pending_commands SET sync_status = ?, attempt_count = attempt_count + 1, last_error = ? WHERE id = ?',
      [status, error, id],
    );
  }

  Future<void> cancelCommand(String id) async {
    await (await _db).update(
      'pending_commands',
      {'sync_status': 'CANCELLED', 'last_error': null},
      where: 'id = ? AND sync_status = ?',
      whereArgs: [id, 'CONFLICT'],
    );
  }
}

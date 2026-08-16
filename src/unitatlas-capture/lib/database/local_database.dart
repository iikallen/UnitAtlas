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
}

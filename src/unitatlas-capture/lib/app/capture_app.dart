import 'package:flutter/material.dart';

import '../capture/pending_command.dart';
import '../sync/capture_repository.dart';

class CaptureApp extends StatelessWidget {
  const CaptureApp({super.key, required this.repository});
  final CaptureRepository repository;

  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'UnitAtlas Capture',
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff3346a8)),
    ),
    home: CaptureHome(repository: repository),
  );
}

class CaptureHome extends StatefulWidget {
  const CaptureHome({super.key, required this.repository});
  final CaptureRepository repository;

  @override
  State<CaptureHome> createState() => _CaptureHomeState();
}

class _CaptureHomeState extends State<CaptureHome> {
  List<PendingCommand> commands = [];
  bool busy = false;
  String? message;

  @override
  void initState() {
    super.initState();
    refresh();
  }

  Future<void> refresh() async {
    final rows = await widget.repository.database.allCommands();
    if (mounted) setState(() => commands = rows);
  }

  Future<void> run(Future<void> Function() action, String success) async {
    setState(() {
      busy = true;
      message = null;
    });
    try {
      await action();
      await refresh();
      if (mounted) setState(() => message = success);
    } catch (error) {
      if (mounted) setState(() => message = error.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('UNITATLAS CAPTURE')),
    body: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Устройство: ${widget.repository.deviceId}'),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: busy
                ? null
                : () =>
                      run(widget.repository.bootstrap, 'Справочники обновлены'),
            child: const Text('ПОЛУЧИТЬ НАСТРОЙКИ'),
          ),
          FilledButton.tonal(
            onPressed: busy
                ? null
                : () => run(widget.repository.sync, 'Синхронизация завершена'),
            child: const Text('СИНХРОНИЗИРОВАТЬ'),
          ),
          if (message != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Text(message!),
            ),
          Text(
            'Локальные команды: ${commands.length}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Expanded(
            child: commands.isEmpty
                ? const Center(child: Text('Очередь пуста'))
                : ListView.builder(
                    itemCount: commands.length,
                    itemBuilder: (_, index) {
                      final command = commands[index];
                      return ListTile(
                        title: Text(
                          '${command.commandType} · ${command.syncStatus}',
                        ),
                        subtitle: Text(
                          command.lastError ??
                              command.createdAt.toLocal().toString(),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    ),
  );
}

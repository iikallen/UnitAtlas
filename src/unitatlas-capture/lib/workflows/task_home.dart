import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../capture/pending_command.dart';
import '../scan/camera_scan_page.dart';
import '../scan/scan_parser.dart';
import '../scan/scan_source.dart';
import '../sync/capture_repository.dart';

class TaskHome extends StatefulWidget {
  const TaskHome({super.key, required this.repository, this.onSignOut});
  final CaptureRepository repository;
  final Future<void> Function()? onSignOut;

  @override
  State<TaskHome> createState() => _TaskHomeState();
}

class _TaskHomeState extends State<TaskHome> {
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

  Future<void> showPilotReport() async {
    final report = await widget.repository.pilotReport();
    if (!mounted) return;
    final text = report.toText(
      deviceId: widget.repository.deviceId,
      station: widget.repository.station,
    );
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Отчёт физического теста'),
        content: SingleChildScrollView(child: SelectableText(text)),
        actions: [
          TextButton(
            onPressed: () async {
              await widget.repository.resetPilotReport();
              if (dialogContext.mounted) Navigator.pop(dialogContext);
            },
            child: const Text('НОВЫЙ ЗАМЕР'),
          ),
          TextButton(
            onPressed: () => Clipboard.setData(ClipboardData(text: text)),
            child: const Text('КОПИРОВАТЬ'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('ЗАКРЫТЬ'),
          ),
        ],
      ),
    );
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

  void open(Widget page) async {
    await Navigator.push(context, MaterialPageRoute(builder: (_) => page));
    await refresh();
  }

  @override
  Widget build(BuildContext context) {
    final pending = commands
        .where((x) => x.syncStatus == 'PENDING' || x.syncStatus == 'RETRY')
        .length;
    final conflicts = commands.where((x) => x.syncStatus == 'CONFLICT').length;
    return Scaffold(
      appBar: AppBar(
        title: const Text('UNITATLAS CAPTURE'),
        actions: [
          IconButton(
            tooltip: 'Отчёт физического теста',
            onPressed: busy ? null : showPilotReport,
            icon: const Icon(Icons.analytics_outlined),
          ),
          if (widget.onSignOut != null)
            IconButton(
              tooltip: 'Выйти',
              onPressed: busy ? null : widget.onSignOut,
              icon: const Icon(Icons.logout),
            ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('Устройство: ${widget.repository.deviceId}'),
          Text('Станция: ${widget.repository.station ?? 'не загружена'}'),
          Text('Очередь: $pending · Конфликты: $conflicts'),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: FilledButton(
                  onPressed: busy
                      ? null
                      : () => run(
                          widget.repository.bootstrap,
                          'Справочники обновлены',
                        ),
                  child: const Text('НАСТРОЙКИ'),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: FilledButton.tonal(
                  onPressed: busy
                      ? null
                      : () => run(
                          widget.repository.sync,
                          'Синхронизация завершена',
                        ),
                  child: const Text('СИНХРОНИЗАЦИЯ'),
                ),
              ),
            ],
          ),
          if (message != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Text(message!),
            ),
          const Divider(height: 32),
          _Task(
            'Производство',
            Icons.precision_manufacturing,
            () => open(ProductionWorkflow(repository: widget.repository)),
            subtitle: 'Подтвердить напечатанную этикетку',
          ),
          _Task(
            'ОТК',
            Icons.fact_check,
            () => open(
              TraceWorkflow(
                repository: widget.repository,
                title: 'ОТК',
                events: const {
                  'PASS': 'QUALITY_PASSED',
                  'FAIL': 'QUALITY_FAILED',
                  'HOLD': 'QUALITY_HOLD',
                },
              ),
            ),
          ),
          _Task(
            'Упаковка',
            Icons.inventory_2,
            () => open(PackagingWorkflow(repository: widget.repository)),
          ),
          _Task(
            'Паллетизация',
            Icons.pallet,
            () => open(
              PackagingWorkflow(
                repository: widget.repository,
                palletization: true,
              ),
            ),
          ),
          _Task(
            'Перемещение',
            Icons.move_to_inbox,
            () => open(
              TraceWorkflow(
                repository: widget.repository,
                title: 'Перемещение',
                events: const {'ПЕРЕМЕСТИТЬ': 'MOVED_TO_WAREHOUSE'},
              ),
            ),
          ),
          _Task(
            'Отгрузка',
            Icons.local_shipping,
            () => open(
              TraceWorkflow(
                repository: widget.repository,
                title: 'Отгрузка',
                events: const {'ОТГРУЗИТЬ': 'SHIPPED'},
              ),
            ),
          ),
          _Task(
            'Приёмка',
            Icons.download_done,
            () => open(
              TraceWorkflow(
                repository: widget.repository,
                title: 'Приёмка',
                events: const {'ПРИНЯТЬ': 'RECEIVED'},
              ),
            ),
          ),
          _Task(
            'Найти товар',
            Icons.search,
            () => open(FindWorkflow(repository: widget.repository)),
          ),
          if (conflicts > 0) ...[
            const Divider(height: 32),
            Text(
              'Конфликты синхронизации',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            ...commands
                .where((x) => x.syncStatus == 'CONFLICT')
                .map(
                  (command) => _ConflictCard(
                    command: command,
                    onCancel: () => run(
                      () => widget.repository.cancelConflict(command.id),
                      'Локальная операция отменена',
                    ),
                  ),
                ),
          ],
        ],
      ),
    );
  }
}

class _ConflictCard extends StatelessWidget {
  const _ConflictCard({required this.command, required this.onCancel});

  final PendingCommand command;
  final Future<void> Function() onCancel;

  Map<String, dynamic> get detail {
    try {
      final value = jsonDecode(command.lastError ?? '{}');
      return value is Map<String, dynamic> ? value : const {};
    } on FormatException {
      return const {};
    }
  }

  Map<String, dynamic> get server {
    final value = detail['server'];
    return value is Map<String, dynamic> ? value : const {};
  }

  String get code =>
      (command.payload['unitAtlasIds'] as List<dynamic>?)?.firstOrNull
          ?.toString() ??
      (command.payload['logisticUnitCodes'] as List<dynamic>?)?.firstOrNull
          ?.toString() ??
      command.payload['unitAtlasId']?.toString() ??
      command.id;

  void showDetails(BuildContext context) {
    final serverParent = server['serverParent']?.toString() ?? 'без упаковки';
    final localParent = command.payload['parentCode']?.toString() ?? '—';
    showDialog<void>(
      context: context,
      builder: (_) => AlertDialog(
        title: Text(code),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Серверная упаковка: $serverParent'),
            Text('Локальная операция: $localParent'),
            if (server['product'] != null) Text('Товар: ${server['product']}'),
            if (server['status'] != null) Text('Статус: ${server['status']}'),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('ЗАКРЫТЬ'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final serverParent = server['serverParent']?.toString() ?? 'без упаковки';
    final localParent = command.payload['parentCode']?.toString() ?? '—';
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.warning_amber, color: Colors.orange),
              title: Text(code),
              subtitle: Text('Сервер: $serverParent\nЛокально: $localParent'),
            ),
            Wrap(
              alignment: WrapAlignment.end,
              children: [
                TextButton(
                  onPressed: () => showDetails(context),
                  child: const Text('ПОСМОТРЕТЬ ИЗДЕЛИЕ'),
                ),
                TextButton(onPressed: onCancel, child: const Text('ОТМЕНИТЬ')),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _Task extends StatelessWidget {
  const _Task(this.label, this.icon, this.onTap, {this.subtitle});
  final String label;
  final IconData icon;
  final VoidCallback? onTap;
  final String? subtitle;

  @override
  Widget build(BuildContext context) => Card(
    child: ListTile(
      leading: Icon(icon),
      title: Text(label),
      subtitle: subtitle == null ? null : Text(subtitle!),
      trailing: onTap == null
          ? const Icon(Icons.lock_clock)
          : const Icon(Icons.chevron_right),
      onTap: onTap,
    ),
  );
}

class ProductionWorkflow extends StatefulWidget {
  const ProductionWorkflow({super.key, required this.repository});
  final CaptureRepository repository;
  @override
  State<ProductionWorkflow> createState() => _ProductionWorkflowState();
}

class _ProductionWorkflowState extends State<ProductionWorkflow> {
  String? scannedCode;
  String location = '';

  Future<void> finish() async {
    if (scannedCode == null || location.trim().isEmpty) return;
    await widget.repository.queueProduction(
      scannedCode: scannedCode!,
      location: location.trim(),
    );
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Производство')),
    body: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ScanInput(
            repository: widget.repository,
            label: 'Сканируйте напечатанную этикетку',
            onScan: (raw) =>
                setState(() => scannedCode = ScanParser.parse(raw).identifier),
          ),
          Text('Изделие: ${scannedCode ?? '—'}'),
          TextField(
            decoration: const InputDecoration(
              labelText: 'Производственная линия',
            ),
            onChanged: (value) => location = value,
          ),
          const SizedBox(height: 16),
          FilledButton(
            onPressed: scannedCode != null && location.trim().isNotEmpty
                ? finish
                : null,
            child: const Text('ПОДТВЕРДИТЬ МАРКИРОВКУ'),
          ),
        ],
      ),
    ),
  );
}

class PackagingWorkflow extends StatefulWidget {
  const PackagingWorkflow({
    super.key,
    required this.repository,
    this.palletization = false,
  });
  final CaptureRepository repository;
  final bool palletization;
  @override
  State<PackagingWorkflow> createState() => _PackagingWorkflowState();
}

class _PackagingWorkflowState extends State<PackagingWorkflow> {
  String? parent;
  final children = <String>[];

  void scan(String raw) {
    final code = ScanParser.parse(raw).identifier;
    setState(() {
      if (parent == null) {
        parent = code;
      } else if (!children.contains(code)) {
        children.add(code);
      }
    });
  }

  Future<void> finish() async {
    if (parent == null || children.isEmpty) return;
    await widget.repository.queueAggregation(
      parentCode: parent!,
      unitAtlasIds: widget.palletization ? const [] : children,
      logisticUnitCodes: widget.palletization ? children : const [],
    );
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(widget.palletization ? 'Паллетизация' : 'Упаковка'),
    ),
    body: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ScanInput(
            repository: widget.repository,
            label: parent == null
                ? 'Сканируйте родительскую упаковку'
                : 'Сканируйте содержимое',
            onScan: scan,
          ),
          const SizedBox(height: 12),
          Text('Родитель: ${parent ?? '—'}'),
          Text('Отсканировано: ${children.length}'),
          Expanded(
            child: ListView(
              children: children.map((x) => ListTile(title: Text(x))).toList(),
            ),
          ),
          FilledButton(
            onPressed: parent != null && children.isNotEmpty ? finish : null,
            child: const Text('ЗАВЕРШИТЬ УПАКОВКУ'),
          ),
        ],
      ),
    ),
  );
}

class TraceWorkflow extends StatefulWidget {
  const TraceWorkflow({
    super.key,
    required this.repository,
    required this.title,
    required this.events,
  });
  final CaptureRepository repository;
  final String title;
  final Map<String, String> events;
  @override
  State<TraceWorkflow> createState() => _TraceWorkflowState();
}

class _TraceWorkflowState extends State<TraceWorkflow> {
  String? unit;
  String location = '';

  Future<void> record(String eventType) async {
    if (unit == null || location.trim().isEmpty) return;
    await widget.repository.queueTrace(
      unitAtlasId: unit!,
      eventType: eventType,
      location: location.trim(),
    );
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text(widget.title)),
    body: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ScanInput(
            repository: widget.repository,
            label: 'Сканируйте изделие',
            onScan: (raw) =>
                setState(() => unit = ScanParser.parse(raw).identifier),
          ),
          Text('Изделие: ${unit ?? '—'}'),
          TextField(
            decoration: const InputDecoration(labelText: 'Место / назначение'),
            onChanged: (value) => location = value,
          ),
          const SizedBox(height: 16),
          ...widget.events.entries.map(
            (entry) => FilledButton(
              onPressed: unit != null && location.trim().isNotEmpty
                  ? () => record(entry.value)
                  : null,
              child: Text(entry.key),
            ),
          ),
        ],
      ),
    ),
  );
}

class FindWorkflow extends StatefulWidget {
  const FindWorkflow({super.key, required this.repository});
  final CaptureRepository repository;
  @override
  State<FindWorkflow> createState() => _FindWorkflowState();
}

class _FindWorkflowState extends State<FindWorkflow> {
  Map<String, dynamic>? result;
  String? error;

  Future<void> resolve(String raw) async {
    try {
      final value = await widget.repository.api.resolve(
        ScanParser.parse(raw).identifier,
      );
      if (mounted) {
        setState(() {
          result = value;
          error = null;
        });
      }
    } catch (value) {
      if (mounted) {
        setState(() {
          error = value.toString();
          result = null;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Найти товар')),
    body: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ScanInput(
            repository: widget.repository,
            label: 'Сканируйте или введите код',
            onScan: resolve,
          ),
          if (result != null) SelectableText(result.toString()),
          if (error != null)
            Text(
              error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
        ],
      ),
    ),
  );
}

class ScanInput extends StatefulWidget {
  const ScanInput({
    super.key,
    required this.repository,
    required this.label,
    required this.onScan,
  });
  final CaptureRepository repository;
  final String label;
  final ValueChanged<String> onScan;
  @override
  State<ScanInput> createState() => _ScanInputState();
}

class _ScanInputState extends State<ScanInput> {
  final keyboardSource = KeyboardWedgeScanSource();
  late final AndroidIntentScanSource intentSource;
  final controller = TextEditingController();
  final subscriptions = <StreamSubscription<String>>[];

  @override
  void initState() {
    super.initState();
    intentSource = AndroidIntentScanSource();
    Future<void> accept(String value) async {
      await widget.repository.recordAcceptedScan();
      widget.onScan(value);
      controller.clear();
    }

    subscriptions.addAll([
      keyboardSource.scans.listen(accept),
      intentSource.scans.listen(accept),
    ]);
  }

  @override
  void dispose() {
    for (final subscription in subscriptions) {
      subscription.cancel();
    }
    keyboardSource.dispose();
    intentSource.dispose();
    controller.dispose();
    super.dispose();
  }

  Future<void> camera() async {
    final value = await Navigator.push<String>(
      context,
      MaterialPageRoute(builder: (_) => const CameraScanPage()),
    );
    if (value != null) keyboardSource.submit(value);
  }

  Future<void> recognitionError() async {
    await widget.repository.recordRecognitionError();
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Ошибка распознавания учтена')),
      );
    }
  }

  @override
  Widget build(BuildContext context) => TextField(
    controller: controller,
    autofocus: true,
    decoration: InputDecoration(
      labelText: widget.label,
      suffixIcon: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            tooltip: 'Код не распознан',
            onPressed: recognitionError,
            icon: const Icon(Icons.report_gmailerrorred),
          ),
          IconButton(
            tooltip: 'Сканировать камерой',
            onPressed: camera,
            icon: const Icon(Icons.qr_code_scanner),
          ),
        ],
      ),
    ),
    onSubmitted: keyboardSource.submit,
  );
}

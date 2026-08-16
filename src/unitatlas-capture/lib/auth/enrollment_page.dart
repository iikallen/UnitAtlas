import 'package:flutter/material.dart';

import '../sync/capture_repository.dart';

class EnrollmentPage extends StatefulWidget {
  const EnrollmentPage({
    super.key,
    required this.repository,
    required this.onEnrolled,
  });

  final CaptureRepository repository;
  final VoidCallback onEnrolled;

  @override
  State<EnrollmentPage> createState() => _EnrollmentPageState();
}

class _EnrollmentPageState extends State<EnrollmentPage> {
  final controller = TextEditingController();
  bool busy = false;
  String? error;

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  Future<void> enroll() async {
    if (controller.text.trim().isEmpty) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.repository.enroll(controller.text);
      widget.onEnrolled();
    } catch (value) {
      if (mounted) setState(() => error = value.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('UNITATLAS CAPTURE')),
    body: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Регистрация устройства',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text('Код устройства: ${widget.repository.deviceId}'),
          const SizedBox(height: 16),
          TextField(
            controller: controller,
            enabled: !busy,
            decoration: const InputDecoration(
              labelText: 'Одноразовый код регистрации',
              border: OutlineInputBorder(),
            ),
            onSubmitted: (_) => enroll(),
          ),
          if (error != null) ...[
            const SizedBox(height: 12),
            Text(
              error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
          const SizedBox(height: 16),
          FilledButton(
            onPressed: busy ? null : enroll,
            child: Text(busy ? 'ПРОВЕРКА…' : 'ЗАРЕГИСТРИРОВАТЬ'),
          ),
        ],
      ),
    ),
  );
}

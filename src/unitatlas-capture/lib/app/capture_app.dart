import 'package:flutter/material.dart';

import '../auth/enrollment_page.dart';
import '../auth/capture_auth.dart';
import '../auth/sign_in_page.dart';
import '../sync/capture_repository.dart';
import '../workflows/task_home.dart';

class CaptureApp extends StatefulWidget {
  const CaptureApp({super.key, required this.repository, required this.auth});
  final CaptureRepository repository;
  final CaptureAuth auth;

  @override
  State<CaptureApp> createState() => _CaptureAppState();
}

class _CaptureAppState extends State<CaptureApp> {
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'UnitAtlas Capture',
    theme: ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xff3346a8)),
      useMaterial3: true,
    ),
    home: widget.auth.requiresSignIn
        ? SignInPage(auth: widget.auth, onSignedIn: () => setState(() {}))
        : widget.repository.isEnrolled
        ? TaskHome(
            repository: widget.repository,
            onSignOut: widget.auth.configured
                ? () async {
                    await widget.auth.signOut();
                    if (mounted) setState(() {});
                  }
                : null,
          )
        : EnrollmentPage(
            repository: widget.repository,
            onEnrolled: () => setState(() {}),
          ),
  );
}

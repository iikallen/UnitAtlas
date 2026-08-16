import 'package:flutter/material.dart';
import 'package:flutter_appauth/flutter_appauth.dart';

import 'capture_auth.dart';

class SignInPage extends StatefulWidget {
  const SignInPage({super.key, required this.auth, required this.onSignedIn});

  final CaptureAuth auth;
  final VoidCallback onSignedIn;

  @override
  State<SignInPage> createState() => _SignInPageState();
}

class _SignInPageState extends State<SignInPage> {
  bool busy = false;
  String? error;

  Future<void> signIn() async {
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.signIn();
      if (widget.auth.isAuthenticated) widget.onSignedIn();
    } on FlutterAppAuthUserCancelledException {
      // Returning from the browser is a normal cancellation, not a failure.
    } catch (value) {
      if (mounted) setState(() => error = value.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  'UnitAtlas Capture',
                  style: Theme.of(context).textTheme.headlineMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 12),
                const Text(
                  'Войдите под учётной записью оператора.',
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 24),
                FilledButton(
                  onPressed: busy ? null : signIn,
                  child: Text(busy ? 'ВХОД…' : 'ВОЙТИ'),
                ),
                if (error != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    ),
  );
}

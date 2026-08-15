let windowStartedAt = 0;
let attempts = 0;

export function takeLoginPermit(now = Date.now()) {
  if (now - windowStartedAt >= 60_000) {
    windowStartedAt = now;
    attempts = 0;
  }
  // ponytail: one-process pilot limiter; replace with a shared gateway policy when login runs on multiple replicas.
  return ++attempts <= 20;
}

# Security Policy

Please do not open a public issue for a suspected vulnerability.

Use GitHub private vulnerability reporting when available, or contact the maintainer privately through the GitHub profile.

AgentMail API keys and webhook secrets are credentials. Never commit them, log them, or include them in telemetry. Webhook handlers should verify the raw request body before deserializing or processing events.

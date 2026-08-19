# Lieth Organigramme Assistant

Offline Windows flowchart editor with ARSEF branding. It stores user settings and the editable local diagram history in `%AppData%\Lieth Organigramme Assistant`, never sends telemetry, and checks only the public GitHub Releases endpoint for updates.

Diva Productivité stores its mission history locally in the same protected Windows user profile. It opens a prefilled email in the computer’s default mail app, where the responsible person reviews it and sends it. Deadlines can be exported as a standard `.ics` calendar event. No password, tenant ID, or cloud configuration is needed.

Updates are optional, downloaded only after confirmation, SHA-256 verified, and installed per-user. The updater backs up the installed app and restores it if installation fails; user settings are outside the install directory and are retained.

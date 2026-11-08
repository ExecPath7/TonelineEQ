# Security

## Reporting a problem

If you find a security problem in Toneline EQ, please **don't open a public issue**. Report it privately instead: on this repository, go to **Security → Report a vulnerability**. You'll get an answer as soon as possible. Once it's fixed, the fix is published and you're credited if you want.

## What's in this repository

- **No secrets.** Toneline EQ uses no API keys, tokens, accounts, passwords or AI services, so there's nothing in this repository that anyone could take and use. The only thing it ever downloads is public AutoEQ data from GitHub, when you search for your headphones.
- **Share codes only carry EQ numbers.** A share code or pasted preset is read as a name plus preamp and filter values. Everything else is thrown away, and names lose line breaks, so a code can't slip Equalizer APO commands (like loading a plugin) into your config. The self-test checks this.
- **No admin rights.** Toneline EQ and its installer run as your normal Windows user.

## Staying safe

- **Only download Toneline EQ from this repository.** Copies posted elsewhere may have been changed.
- Toneline EQ isn't code-signed, so Windows SmartScreen may warn you the first time. That's why it ships as source: `Install Toneline EQ.cmd` builds it on your own PC with Windows' own compiler, so you can read every line first.
- Toneline EQ installs for your account only and never asks for administrator rights. If a copy of "Toneline EQ" asks for them, don't run it.
- Toneline EQ collects no data. See [PRIVACY.txt](PRIVACY.txt).

## Supported versions

Only the latest version gets fixes.

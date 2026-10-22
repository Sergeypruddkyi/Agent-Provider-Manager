# Agent Provider Manager

![Agent Provider Manager](assets/agent-provider-manager.png)

A minimal Windows launcher for managing **Claude Code** and **OpenAI
Codex** providers: enter the Base URL, API key, model, and working
directory, then launch the selected tool in a separate interactive
console window with those settings.

The application is **not** a full-featured provider manager: it has no
database, history, logging, proxy, IPC, or automatic model discovery.
Each launch is independent.

------------------------------------------------------------------------

## Requirements

-   Windows x64
-   .NET 10 SDK (for building only)
-   Claude Code CLI installed (`claude` command available in `PATH`) to
    use Claude Code
-   OpenAI Codex CLI installed (`codex` command available in `PATH`) to
    use Codex

Check the installed Claude Code version:

``` powershell
claude --version   # version at the time of development: 2.1.288
claude --help
```

------------------------------------------------------------------------

## Build a portable EXE (win-x64, self-contained)

Command to build the final distribution:

``` powershell
dotnet publish "src\\ClaudeProviderLauncher\\ClaudeProviderLauncher.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o "dist\\AgentProviderLauncher"
```

Path to the generated executable:

``` text
E:\\Projects\\Agent Provider Manager\\dist\\AgentProviderLauncher\\ClaudeProviderLauncher.exe
```

------------------------------------------------------------------------

## Quick Start and Usage

### Quick start

1.  Open the distribution directory `dist\\AgentProviderLauncher\\`.
2.  Run `ClaudeProviderLauncher.exe` (double-click it or launch it from
    PowerShell/CMD).
3.  On Windows x64, installing the .NET Runtime or SDK on the target
    computer is not required.

### Using the application

1.  Run `ClaudeProviderLauncher.exe`.
2.  Select a tool at the top of the window: **Claude Code** or
    **Codex**.
3.  Optionally select a saved **preset** from the dropdown (see the
    Presets section below). It fills in the Base URL, key, and model for
    the selected tool.
4.  Fill in:
    -   **Base URL** --- the provider API root (see the Base URL section
        below);
    -   **API Key / Auth Token** --- the key or token (the field is
        masked);
    -   **Model** --- the model identifier (case may matter for
        different providers);
    -   **Working Directory** --- an existing project folder (use
        **Browse...**).
5.  Click **START** (the button is **START CLAUDE CODE** or **START
    CODEX**, depending on the selected tool).
6.  The selected tool opens in a separate interactive window in the
    chosen directory.

The most recently entered Base URL, Model, and Working Directory are
remembered for convenience. The API key is **never** saved in the
recent-values file (it is stored only in presets; see below).

------------------------------------------------------------------------

## Presets

The compact dropdown stores named combinations of **Base URL + API Key /
Auth Token + Model**, separately for each tool:

-   **Save Preset** --- asks for a name and saves the current three
    values (if the name already exists, it offers to overwrite it);
-   **Overwrite** --- replaces the selected preset with the current
    field values;
-   **Delete** --- deletes the selected preset after confirmation.

Selecting a preset fills in the three fields automatically. The Working
Directory is not part of a preset and does not change when a preset is
selected; the project directory is set separately for each launch.

Claude Code and Codex presets are stored separately in
`%LOCALAPPDATA%\\ClaudeProviderLauncher\\presets.json`, alongside the
launcher's other settings, and persist across application restarts. If
no presets exist, the application works as before. The key is encrypted
in the file using Windows DPAPI (`CurrentUser` scope); only the current
Windows user on that PC can decrypt it. If the file is unreadable,
belongs to another user, or is corrupted, the key is treated as empty
and must be entered again.

------------------------------------------------------------------------

## How It Works

Configuration is passed **only through the child process's environment
variables**. The application's global Windows environment variables and
the tools' configuration files are not modified.

### Claude Code

  -------------------------------------------------------------------------------------------
  Variable                            Value
  ----------------------------------- -------------------------------------------------------
  `ANTHROPIC_BASE_URL`                Base URL from the field (normalized as described below)

  `ANTHROPIC_AUTH_TOKEN`              The entered key/token

  `ANTHROPIC_MODEL`                   Model from the field

  `CLAUDE_CONFIG_DIR`                 Separate configuration directory:
                                      `%LOCALAPPDATA%\\ClaudeProviderLauncher\\claude-home`
  -------------------------------------------------------------------------------------------

The model is also passed using the `--model <model>` argument (the CLI
argument has the highest priority).

#### Why a separate `CLAUDE_CONFIG_DIR`?

Values in the `env` block of `~/.claude/settings.json` **override** the
process environment variables (this is confirmed by the documentation
and issue anthropics/claude-code#8500). For example, if
`~/.claude/settings.json` already specifies another provider, it could
override the launcher's settings. The launcher therefore starts Claude
Code with a separate configuration directory: the user's
`~/.claude/settings.json` is neither read nor modified, and the
launcher's values are the single source of configuration. Each launch is
independent.

In this directory, the launcher also marks onboarding as complete and
pre-approves trust for the selected working directory so the tool can
start with one click.

### Codex

Codex is launched with the `-c` option to override configuration and the
`OPENAI_API_KEY` environment variable for the API key. The file
`~/.codex/config.toml` is read-only and is **never modified** by the
application. When using the **Default: Codex (config.toml)** profile,
Codex starts without additional arguments, preserving the user's
existing configuration (ChatGPT sign-in or configured provider).

------------------------------------------------------------------------

## Base URL: Do Not Include a Trailing `/v1`

Tested with Claude Code version 2.1.288: Claude Code **automatically
appends `/v1/messages`** to `ANTHROPIC_BASE_URL`. Therefore, the URL
should specify the API root without a trailing `/v1`:

-   `https://anymodel.org/v1` → the request goes to `/v1/v1/messages` →
    error;
-   `https://anymodel.org` → the request goes to `/v1/messages` →
    correct.

The launcher automatically removes a trailing `/v1`, so you may enter
`https://anymodel.org/v1`. Any other path is preserved; for example,
`https://host/api` correctly becomes `https://host/api/v1/messages`.

------------------------------------------------------------------------

## Provider Requirement: Anthropic Messages API

Claude Code uses the **Anthropic Messages** protocol
(`POST /v1/messages`). The provider must support it.

Tested directly:

  -------------------------------------------------------------------------------------------
  Provider                       `/v1/messages`    `/v1/chat/completions`   Compatible with
                                                   (OpenAI)                 Claude Code
  ------------------------------ ----------------- ------------------------ -----------------
  AnyModel                       available (401    ---                      **yes**
  (`https://anymodel.org`)       with an invalid                            
                                 key)                                       

  Top Tools                      **404**           200 OK                   **no** (OpenAI
  (`https://top-tools-ai.com`)                                              only)

  Code Craft                     **404**           200 OK                   **no** (OpenAI
  (`https://codecraftapi.com`)                                              only)
  -------------------------------------------------------------------------------------------

If a provider supports only the OpenAI format (`/chat/completions`), it
will **not work** directly with Claude Code. A separate protocol
translation proxy is required (for example, `CLIProxyAPI`) to expose
Anthropic `POST /v1/messages`.

------------------------------------------------------------------------

## Security

-   The key is passed only through the child process environment.
-   The key is **not** included in the command line, `.cmd`/`.ps1`
    files, or logs, and is not saved in recent values.
-   In presets, the key is stored **only in encrypted form** (DPAPI,
    `CurrentUser`) in
    `%LOCALAPPDATA%\\ClaudeProviderLauncher\\presets.json`, outside the
    project directory and Git.
-   Global Windows environment variables are not modified.
-   `~/.claude/settings.json` is neither read nor modified.

------------------------------------------------------------------------

## Tests

``` powershell
dotnet test "tests\\ClaudeProviderLauncher.Tests\\ClaudeProviderLauncher.Tests.csproj" -c Release
```

Coverage includes saving/loading presets, selection, overwriting and
deleting presets, encrypting keys on disk, and handling corrupted files.

------------------------------------------------------------------------

## Intentionally Out of Scope

The application does not include a system of profiles (presets contain
only three provider values, not folders or launch settings), a database,
IPC, Named Pipes, a separate runner, API compatibility checks, automatic
model discovery, AWS Bedrock, Google Vertex, OAuth, launch history,
logging, an embedded terminal, `settings.json` editing, system
environment-variable changes, or automatic installation/updating of
Claude Code.

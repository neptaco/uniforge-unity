# UniForge for Unity

This package lets the UniForge CLI work with an open Unity Editor. It provides commands for inspecting scenes, changing GameObjects and components, working with assets and prefabs, controlling Play Mode, and running tests.

The package connects your Editor to the local [UniForge CLI](https://github.com/neptaco/uniforge). No cloud service or project upload is required.

## Requirements

- Unity 6.0 LTS or later (`6000.0+`)
- [UniForge CLI](https://github.com/neptaco/uniforge#installation)

## Install

First, move to the Unity project root—the directory that contains `Assets`, `Packages`, and `ProjectSettings`:

```bash
cd /path/to/MyUnityProject
```

Then install the package with the UniForge CLI:

```bash
uniforge package add neptaco/uniforge-unity/Packages/dev.crysta.uniforge
```

When the project argument is omitted, UniForge detects the Unity project containing the current directory. The short GitHub source is expanded to the package's HTTPS Git URL, and the highest semantic-version tag is selected automatically. Before changing `Packages/manifest.json`, UniForge compares the package's minimum Unity version with the version in `ProjectSettings/ProjectVersion.txt`. Newer streams, including alpha and beta releases, are accepted when they meet that minimum. Older or unparseable versions stop without changing the project. Existing manifest dependencies are preserved.

To run the command from another directory, pass the project path explicitly:

```bash
uniforge package add /path/to/MyUnityProject neptaco/uniforge-unity/Packages/dev.crysta.uniforge
```

In an interactive terminal, UniForge shows the project Unity version, package compatibility, resolved URL, tag, package reference, and manifest path before making the change. Enter `n` to cancel, or add `--yes` to skip this confirmation. Non-interactive commands skip the prompt so CI and coding agents do not wait for input, but they still run the compatibility check. Use `--force` only when you intentionally want to bypass a failed or unavailable check.

To pin a release:

```bash
uniforge package add neptaco/uniforge-unity/Packages/dev.crysta.uniforge --tag v0.11.0
```

The full URL form is also accepted when the tag is passed separately:

```bash
uniforge package add "https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge" --tag v0.11.0
```

Alternatively, open **Window > Package Management > Package Manager**, choose **Install package from git URL**, and paste:

```text
https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge#v0.11.0
```

The release tag keeps your project on a known package version. Check [Tags](https://github.com/neptaco/uniforge-unity/tags) for newer versions.

## Try It

Keep the Unity Editor open. UniForge connects automatically, and CLI tool commands start the local daemon on demand.

Confirm that your project is connected:

```bash
uniforge tool projects
```

Inspect the active scene hierarchy:

```bash
uniforge tool call hierarchy
```

Inspect a GameObject:

```bash
uniforge tool call gameobject '{"path":"Main Camera"}'
```

Run EditMode tests without closing the Editor:

```bash
uniforge tool call run-tests '{"mode":"EditMode"}'
```

Tool output is YAML by default. Add `-o json` when you need JSON.

## Coding Agent Skill

The UniForge repository includes a skill with the setup, command selection, testing, and troubleshooting guidance needed to use these tools consistently from a coding agent. Install it from your project root:

```bash
npx skills add neptaco/uniforge --skill uniforge
```

The command requires Node.js. The installer detects supported coding agents and uses project scope by default. Add `--global` to make the skill available in every project.

## Available Tools

| Task | Examples |
|---|---|
| Scenes and objects | Inspect the hierarchy, create or delete GameObjects, change transforms and parents |
| Components | Add or remove components, read and update serialized properties |
| Assets and prefabs | Search assets, create materials, create/apply/revert prefabs |
| Editor control | Enter or exit Play Mode, compile scripts, execute menu items, frame objects |
| Testing and debugging | List and run tests, read logs, wait for log messages, capture Editor windows |
| Play Mode interaction | Send supported input and run repeatable Play Mode scenarios |

Projects and installed packages can provide additional tools. Use these commands to see the tools available in the open project and the arguments they accept:

```bash
uniforge tool list
```

```bash
uniforge tool describe create-gameobject
```

## Connection and Project Selection

Open **Window > UniForge > Connection** to view the connection status or change auto-connect behavior. Open **Window > UniForge > Tool List** to inspect the tools exposed by the current project.

If more than one Unity Editor is connected, target one by project name:

```bash
uniforge tool call hierarchy --project my-game
```

The package communicates with the UniForge daemon over local IPC. Tool calls are routed only to a connected local Editor.

## Updating

After the first installation, update the Git tag in Package Manager or let the CLI update the existing package reference:

```bash
uniforge package update
```

Review and commit resulting changes to `Packages/manifest.json` and `Packages/packages-lock.json` after an update.

## Troubleshooting

- **No connected projects:** keep Unity open, check **Window > UniForge > Connection**, then run `uniforge tool projects` again.
- **A tool needs arguments:** run `uniforge tool describe <tool-name>` to see its input schema.
- **The Editor is closed:** live tools are unavailable; use `uniforge compile`, `uniforge test`, or `uniforge run` from the CLI instead.
- **The daemon looks stale:** run `uniforge daemon restart`, then reconnect from the Unity window.

## License

[MIT](Packages/dev.crysta.uniforge/LICENSE.md)

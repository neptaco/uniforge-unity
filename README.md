# UniForge Unity Client

Unity Editor 側の UniForge 統合パッケージ。Go CLI daemon と JSON-RPC で通信し、AI エージェントからの Unity 操作を実現します。

## Requirements

- Unity 6.0 LTS 以降 (6000.0+)
- [UniForge CLI](https://github.com/neptaco/uniforge)

## Installation (UPM)

Unity Package Manager の "Add package from git URL" で以下を指定:

```
https://github.com/neptaco/uniforge-unity.git?path=Packages/dev.crysta.uniforge#v0.9.0
```

## Package Structure

```
Packages/
├── dev.crysta.uniforge/          # メインパッケージ
│   └── Editor/
│       ├── Bridge/                # Daemon 通信 (JSON-RPC transport)
│       ├── Tools/                 # Tool framework / queries / mutations
│       │   ├── Framework/         # ToolAttribute, ToolResult, ToolRegistry
│       │   ├── Queries/           # 読み取り系ツール (gameobject, hierarchy, etc.)
│       │   └── Mutations/         # 変更系ツール (create, modify, prefab, etc.)
│       ├── UI/                    # EditorWindow
│       └── Common/                # 共通ユーティリティ
└── dev.crysta.uniforge.tests/    # テストパッケージ
```

## Usage

1. UniForge CLI daemon を起動: `uniforge daemon start`
2. Unity Editor でプロジェクトを開く
3. 自動的に daemon に接続される
4. CLI または MCP 経由でツールを実行可能

```bash
uniforge tool list                           # ツール一覧
uniforge tool call editor-state -o json      # エディタ状態取得
uniforge tool call gameobject '{"path":"Main Camera"}' -o json
```

## License

MIT License

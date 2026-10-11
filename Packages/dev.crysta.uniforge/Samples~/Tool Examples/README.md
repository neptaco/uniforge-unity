# Tool Examples

Add your own UniForge tools with Editor code in your Unity project.
These examples show queries, undoable mutations, and operations that span a domain reload.

## Included tools

| Tool | Base class | Shows |
| --- | --- | --- |
| `example-scene-summary` | `QueryHandler` | Optional filters and structured scene counts |
| `example-rename-gameobject` | `MutationHandler` | Required arguments, hierarchy lookup, validation, and Undo |
| `example-reload-domain` | `DomainReloadResumableMutationHandler<State>` | Serializable state, acknowledgement, polling, and timeout |

## Try it

In Package Manager, select UniForge and import **Tool Examples** from **Samples**.
After Unity compiles the imported code and the Editor reconnects, the tools appear in
`uniforge tool list`.

```sh
uniforge tool list
uniforge tool call example-scene-summary '{"include_inactive":true}'
uniforge tool call example-rename-gameobject '{"path":"Main Camera","new_name":"Renamed Camera"}'
uniforge tool call example-reload-domain
```

Use a scene containing `Main Camera`, or replace the path with an existing GameObject's path.
One **Edit > Undo** restores the rename.
The reload example keeps the call open across the reload and returns once the new domain
is loaded.

## Writing your own tool

- Put code in an Editor-only assembly referencing `UniForge` in its asmdef, or in your
  project's `Editor` folder. Assemblies starting with `Unity`, `System`, `mscorlib`, or
  `netstandard` are excluded from discovery.
- Add `[Tool("kebab-case-name")]` to a non-abstract class derived from `QueryHandler`
  for read-only operations or `MutationHandler` for changes. A parameterless constructor
  is required.
- Override `Execute` with `protected override`; `protected internal` cannot be used
  for an override outside the package assembly. For asynchronous work, override
  `ExecuteAsync`, returning `Awaitable<ToolResult>`.
- Define a nested `Args` class with `[ToolParameter]` fields, read arguments with
  `ToolArgsParser`, and declare structured output with `[ToolOutput(typeof(...))]`.
- Return `ToolResult.Ok(object)` or `ToolResult.Fail(string)`. Result payload types
  must serialize to a JSON object; a standalone string or bool is not valid.
- Record mutations with Unity's Undo API. `Execute` runs in one Undo group named after
  the tool's Title; each resume after a domain reload records into a new group.
- To span a domain reload, use `DomainReloadResumableQueryHandler<TState>` or
  `DomainReloadResumableMutationHandler<TState>`. TState must be a class serializable
  by JsonUtility. Handler instances and static values do not survive a reload;
  save required values in TState's public fields. Resume may also run before reload.
- Avoid names that collide with built-in tools; check `uniforge tool list`.

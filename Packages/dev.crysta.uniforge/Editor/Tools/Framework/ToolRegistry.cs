using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// Note: ToolSettings is in UniForge namespace, accessed via full path
namespace UniForge.Tools
{
    /// <summary>
    /// ツールの登録・検索を管理するレジストリ
    /// </summary>
    public class ToolRegistry
    {
        private readonly Dictionary<string, IToolHandler> _handlers = new();
        private readonly Dictionary<string, IDomainReloadResumableTool> _domainReloadResumableHandlers = new();

        /// <summary>登録されているツール数</summary>
        public int Count => _handlers.Count;

        /// <summary>
        /// アセンブリ内の [Tool] 属性が付いたハンドラーを自動登録
        /// </summary>
        public void RegisterFromAssembly(Assembly assembly = null)
        {
            assembly ??= Assembly.GetExecutingAssembly();

            var handlerInterface = typeof(IToolHandler);
            var handlerTypes = assembly.GetTypes()
                .Where(t => handlerInterface.IsAssignableFrom(t)
                         && !t.IsAbstract
                         && !t.IsInterface
                         && t.GetCustomAttribute<ToolAttribute>()?.Register != false);

            foreach (var type in handlerTypes)
            {
                try
                {
                    var handler = (IToolHandler)Activator.CreateInstance(type);
                    Register(handler);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[ToolRegistry] Failed to create handler {type.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 全ての [Tool] 属性付きハンドラーを検索して自動登録
        /// </summary>
        public void RegisterAllToolHandlers()
        {
            // UniForge アセンブリから登録
            RegisterFromAssembly(typeof(ToolRegistry).Assembly);

            // 他のアセンブリからも検索（ユーザー定義ツール対応）
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Unity/System系のアセンブリはスキップ（UniForge系は除く）
                var name = assembly.GetName().Name;
                if (name.StartsWith("UniForge"))
                {
                    // UniForge.* アセンブリは登録対象（Addressables等）
                    // ただし既に登録済みのメインアセンブリはスキップ
                    if (name == "UniForge")
                    {
                        continue;
                    }
                }
                else if (name.StartsWith("Unity") ||
                         name.StartsWith("System") ||
                         name.StartsWith("mscorlib") ||
                         name.StartsWith("netstandard"))
                {
                    continue;
                }

                try
                {
                    RegisterFromAssembly(assembly);
                }
                catch
                {
                    // アセンブリ読み込みエラーは無視
                }
            }
        }

        /// <summary>ツールハンドラーを登録</summary>
        public void Register(IToolHandler handler)
        {
            var toolName = handler.Definition.name;
            _handlers[toolName] = handler;

            if (handler is IDomainReloadResumableTool resumableHandler)
            {
                _domainReloadResumableHandlers[toolName] = resumableHandler;
            }
            else
            {
                _domainReloadResumableHandlers.Remove(toolName);
            }
        }

        /// <summary>複数のツールハンドラーを登録</summary>
        public void RegisterAll(params IToolHandler[] handlers)
        {
            foreach (var handler in handlers)
            {
                Register(handler);
            }
        }

        /// <summary>ツールを登録解除</summary>
        public void Unregister(string name)
        {
            _handlers.Remove(name);
            _domainReloadResumableHandlers.Remove(name);
        }

        /// <summary>ツールハンドラーを取得</summary>
        public IToolHandler GetHandler(string name)
        {
            return _handlers.TryGetValue(name, out var handler) ? handler : null;
        }

        /// <summary>Get a domain-reload resumable handler by tool name.</summary>
        public IDomainReloadResumableTool GetDomainReloadResumableHandler(string name)
        {
            return _domainReloadResumableHandlers.TryGetValue(name, out var handler) ? handler : null;
        }

        /// <summary>ツールが存在するか確認</summary>
        public bool HasTool(string name)
        {
            return _handlers.ContainsKey(name);
        }

        /// <summary>全ツール定義を取得</summary>
        public IEnumerable<ToolDefinition> GetAllDefinitions()
        {
            return _handlers.Values.Select(h => h.Definition);
        }

        /// <summary>クエリツール定義のみ取得</summary>
        public IEnumerable<ToolDefinition> GetQueryDefinitions()
        {
            return GetAllDefinitions().Where(d => d.IsQuery);
        }

        /// <summary>ミューテーションツール定義のみ取得</summary>
        public IEnumerable<ToolDefinition> GetMutationDefinitions()
        {
            return GetAllDefinitions().Where(d => d.IsMutation);
        }

        /// <summary>デーモン登録用のツール一覧を生成（全ツール）</summary>
        public List<Dictionary<string, object>> ToRegistrationList()
        {
            return _handlers.Values
                .Select(h => h.Definition.ToRegistrationObject())
                .ToList();
        }

        /// <summary>デーモン登録用のツール一覧を生成（有効なツールのみ）</summary>
        public List<Dictionary<string, object>> ToEnabledRegistrationList()
        {
            var settings = ToolSettings.instance;
            return _handlers.Values
                .Where(h => settings.IsToolEnabled(h.Definition.name))
                .Select(h => h.Definition.ToRegistrationObject())
                .ToList();
        }

        /// <summary>有効なツール定義のみ取得</summary>
        public IEnumerable<ToolDefinition> GetEnabledDefinitions()
        {
            var settings = ToolSettings.instance;
            return GetAllDefinitions().Where(d => settings.IsToolEnabled(d.name));
        }

        /// <summary>有効なツール数を取得</summary>
        public int EnabledCount
        {
            get
            {
                var settings = ToolSettings.instance;
                return _handlers.Values.Count(h => settings.IsToolEnabled(h.Definition.name));
            }
        }

        /// <summary>レジストリをクリア</summary>
        public void Clear()
        {
            _handlers.Clear();
            _domainReloadResumableHandlers.Clear();
        }
    }
}

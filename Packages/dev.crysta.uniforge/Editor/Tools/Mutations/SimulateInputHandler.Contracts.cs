using System.Collections.Generic;
using UniForge.Services;

namespace UniForge.Tools.Mutations
{
    public partial class SimulateInputHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Input action type", Required = true,
                Enum = "key_down,key_up,key_press,mouse_down,mouse_up,mouse_click,mouse_move,mouse_scroll,tap,drag,long_press,tap_ui,input_text,wait")]
            public string action;

            [ToolParameter("Key name for keyboard input (e.g., 'W', 'Space', 'LeftShift', 'Escape')", Required = false)]
            public string key;

            [ToolParameter("Mouse button for mouse input (0=left, 1=right, 2=middle)", Required = false)]
            public int? button;

            [ToolParameter("X position for mouse input (Unity screen coordinates, or world coordinates if coordinate='world')", Required = false)]
            public float? x;

            [ToolParameter("Y position for mouse input (Unity screen coordinates, or world coordinates if coordinate='world')", Required = false)]
            public float? y;

            [ToolParameter("Scroll delta for mouse_scroll action", Required = false)]
            public float? scroll_delta;

            [ToolParameter("Duration in milliseconds for key_press/drag/long_press actions (default: 100 for key_press, 500 for drag)", Required = false)]
            public int? duration_ms;

            [ToolParameter("Coordinate system: 'screen' (Unity screen coords, default) or 'world'", Required = false)]
            public string coordinate;

            [ToolParameter("Start position [x,y] for drag action (world or screen coordinates)", Required = false)]
            public float[] from;

            [ToolParameter("End position [x,y] for drag action (world or screen coordinates)", Required = false)]
            public float[] to;

            [ToolParameter("Position [x,y] for tap/long_press actions (world or screen coordinates)", Required = false)]
            public float[] position;

            [ToolParameter("Hierarchy path of UI element for tap_ui/input_text (e.g., 'Canvas/Panel/Button')", Required = false)]
            public string path;

            [ToolParameter("Name of UI element for tap_ui/input_text", Required = false)]
            public string name;

            [ToolParameter("Instance ID of target GameObject for tap_ui/input_text", Required = false)]
            public int? instance_id;

            [ToolParameter("Text to input for input_text action", Required = false)]
            public string text;

            [ToolParameter("If true, append text instead of replacing for input_text (default: false)", Required = false)]
            public bool? append;

            [ToolParameter("Wait time in milliseconds for wait action", Required = false)]
            public int? ms;

            [ToolParameter("Wait time in milliseconds after action execution. The game continues running during this wait. Logs generated during this period are included in the response.", Required = false)]
            public int? wait_ms;

            [ToolParameter("Log type filter for collected logs: 'all', 'errors', 'warnings', 'info'", Required = false, Default = "all")]
            public string log_filter;

            [ToolParameter("Maximum number of logs to collect", Required = false, Default = 50)]
            public int log_limit;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string action;
            public string details;
            public string message;
            public string simulator_type;
            public AutoPlayService.UiHitCompact hit_ui;
            public List<AutoPlayService.UiHitCompact> ui_hits;
        }
    }
}

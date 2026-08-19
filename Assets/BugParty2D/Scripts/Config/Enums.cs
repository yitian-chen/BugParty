using UnityEngine;

namespace BugParty.TopDown2D
{
    public enum PlayerColor { Red = 0, Blue = 1, Yellow = 2, Green = 3 }

    public enum ItemCategory { Fishing = 0, Destruction = 1, Cooking = 2, Police = 3 }

    public enum RoomTheme { Fishing = 0, Cooking = 1, Police = 2 }

    /// <summary>回合阶段。终局多了 Collapse（全塌陷）与 Transition（穿越到下一关）。</summary>
    public enum RoundPhase
    {
        /// <summary>入场：门关闭，不可操作</summary>
        Intro = 0,
        /// <summary>搜索：可自由行动，少量地板会随机塌陷</summary>
        Searching = 1,
        /// <summary>★全塌陷：所有地板波浪式塌陷，玩家依次掉落</summary>
        Collapse = 2,
        /// <summary>★穿越：黑屏/白光过渡，衔接下一关</summary>
        Transition = 3,
        /// <summary>结束</summary>
        Finished = 4
    }

    /// <summary>★地板块的四态。</summary>
    public enum TileState
    {
        /// <summary>完好，可通行</summary>
        Solid = 0,
        /// <summary>开裂预警中，仍可通行但即将塌陷</summary>
        Cracking = 1,
        /// <summary>已塌陷，碰撞体移除，掉下去要受罚</summary>
        Collapsed = 2,
        /// <summary>正在下坠（终局动画用）</summary>
        Falling = 3
    }

    /// <summary>玩家在垂直方向的状态。2D 俯视但有高度差，需要区分。</summary>
    public enum VerticalState
    {
        /// <summary>站在某个平面上</summary>
        Grounded = 0,
        /// <summary>跳跃上升中</summary>
        Rising = 1,
        /// <summary>下落中</summary>
        Falling = 2,
        /// <summary>★掉进塌陷的洞里</summary>
        Pitfall = 3
    }

    public static class PlayerColorExtensions
    {
        public static Color ToColor(this PlayerColor c)
        {
            switch (c)
            {
                case PlayerColor.Red:    return new Color(0.90f, 0.24f, 0.24f);
                case PlayerColor.Blue:   return new Color(0.21f, 0.54f, 0.87f);
                case PlayerColor.Yellow: return new Color(0.94f, 0.75f, 0.18f);
                case PlayerColor.Green:  return new Color(0.36f, 0.72f, 0.30f);
                default:                 return Color.gray;
            }
        }

        public static string ToLabel(this PlayerColor c)
        {
            switch (c)
            {
                case PlayerColor.Red:    return "红";
                case PlayerColor.Blue:   return "蓝";
                case PlayerColor.Yellow: return "黄";
                case PlayerColor.Green:  return "绿";
                default:                 return "?";
            }
        }
    }
}

using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>控制器基类。真人与 AI 共用 PlayerActor，只换这一层。</summary>
    [RequireComponent(typeof(PlayerActor))]
    public abstract class PlayerBrain : MonoBehaviour
    {
        protected PlayerActor Actor { get; private set; }
        protected RoomConfig Cfg { get; private set; }

        protected virtual void Awake() => Actor = GetComponent<PlayerActor>();

        protected virtual void Start()
            => Cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;

        protected virtual void Update()
        {
            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct || !Actor.IsAlive
                || Actor.IsStaggered || Actor.IsInPitfall)
            {
                Actor.MoveInput = Vector2.zero;
                return;
            }
            Think();
        }

        protected abstract void Think();
    }

    /// <summary>一套按键映射，支持本地四人同屏。</summary>
    [System.Serializable]
    public class InputScheme
    {
        public KeyCode up = KeyCode.W;
        public KeyCode down = KeyCode.S;
        public KeyCode left = KeyCode.A;
        public KeyCode right = KeyCode.D;

        [Tooltip("按住搜索")]
        public KeyCode search = KeyCode.J;

        [Tooltip("肘击")]
        public KeyCode elbow = KeyCode.K;

        [Tooltip("★跳跃")]
        public KeyCode jump = KeyCode.Space;

        public Vector2 ReadMove()
        {
            float x = 0f, y = 0f;
            if (Input.GetKey(left)) x -= 1f;
            if (Input.GetKey(right)) x += 1f;
            if (Input.GetKey(down)) y -= 1f;
            if (Input.GetKey(up)) y += 1f;
            return new Vector2(x, y);
        }

        public static InputScheme Player1() => new InputScheme
        {
            up = KeyCode.W, down = KeyCode.S, left = KeyCode.A, right = KeyCode.D,
            search = KeyCode.J, elbow = KeyCode.K, jump = KeyCode.Space
        };

        public static InputScheme Player2() => new InputScheme
        {
            up = KeyCode.UpArrow, down = KeyCode.DownArrow,
            left = KeyCode.LeftArrow, right = KeyCode.RightArrow,
            search = KeyCode.Keypad1, elbow = KeyCode.Keypad2, jump = KeyCode.Keypad0
        };

        public static InputScheme Player3() => new InputScheme
        {
            up = KeyCode.T, down = KeyCode.G, left = KeyCode.F, right = KeyCode.H,
            search = KeyCode.V, elbow = KeyCode.B, jump = KeyCode.R
        };

        public static InputScheme Player4() => new InputScheme
        {
            up = KeyCode.I, down = KeyCode.K, left = KeyCode.J, right = KeyCode.L,
            search = KeyCode.N, elbow = KeyCode.M, jump = KeyCode.O
        };
    }

    /// <summary>真人控制器。</summary>
    public class HumanBrain : PlayerBrain
    {
        [Header("按键")]
        public InputScheme keys = new InputScheme();

        [Header("操作方向")]
        [Tooltip("勾选后 WASD 按屏幕方向走。2D 俯视角强烈建议勾上")]
        public bool cameraRelative = true;

        Transform _cam;

        protected override void Start()
        {
            base.Start();
            if (Camera.main != null) _cam = Camera.main.transform;
        }

        protected override void Update()
        {
            // 跳跃输入要在基类的门禁之外读取，保证按键缓冲始终有效
            var mgr = RoomManager.Instance;
            if (mgr != null && mgr.CanAct && Actor.IsAlive && Input.GetKeyDown(keys.jump))
                Actor.WantJump = true;

            base.Update();
        }

        protected override void Think()
        {
            var raw = keys.ReadMove();
            Actor.MoveInput = cameraRelative ? ToCameraSpace(raw) : raw;

            if (Input.GetKeyDown(keys.search))
                Actor.Search.TryBegin();
            else if (Input.GetKeyUp(keys.search) && Actor.Search.IsSearching)
                Actor.Search.Cancel(false);

            if (Input.GetKeyDown(keys.elbow))
                Actor.Elbow.TryElbow();
        }

        /// <summary>把输入从屏幕空间转到世界空间。</summary>
        Vector2 ToCameraSpace(Vector2 raw)
        {
            if (_cam == null || raw.sqrMagnitude < 0.0001f) return raw;

            var fwd = _cam.forward; fwd.y = 0f;
            var right = _cam.right; right.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return raw;

            fwd.Normalize();
            right.Normalize();

            var world = fwd * raw.y + right * raw.x;
            return new Vector2(world.x, world.z);
        }
    }
}

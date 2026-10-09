using UnityEngine;

namespace ActionGame.Sim
{
    /// <summary>
    /// 一个角色这一 tick 想做什么。**谁产出的它不管**——玩家输入、AI、以后的录像回放，
    /// 都只是「有人把这三个字段填好」，然后交给 <c>PlayerSim.Tick</c>。
    ///
    /// 这就是「操作」的接口：把「谁在操作」和「角色会做什么」拆开的那道缝。
    /// 生产者那一侧负责把各自的源翻译成世界空间意图（玩家用 <see cref="FromPlayerInput"/>，
    /// AI 直接填）；角色那一侧只消费结果——不认识键鼠、不认识相机、不认识 <c>InputFrame</c>。
    ///
    /// 它和 <see cref="InputFrame"/> 的分工：
    ///   InputFrame   = **设备输入**（含鼠标位移）：相机的口粮 + 玩家路径的原料；
    ///   ActorCommand = **角色意图**（世界空间）：角色的口粮。
    /// 一帧只有一份 InputFrame，但**将来每个角色各有一份 ActorCommand**。
    ///
    /// 本文件用了 <c>Vector3</c> / <c>Mathf</c>，属铁律 1 放行的无状态数学类型。
    /// </summary>
    public struct ActorCommand
    {
        /// <summary>
        /// 移动意图，**世界空间**。长度 ≤ 1：0 = 没有移动输入；0.5 = 慢走（摇杆半推）。
        /// （角色那边还会再压一次长度兜底——生产者若给了长度 2 的方向，速度不能跟着翻倍。）
        /// </summary>
        public Vector3 MoveDirection;

        /// <summary>
        /// 想面朝的偏航角（度）。玩家路径填相机 yaw；AI 填它盯着谁。
        /// 「处于攻击中就不转」那条锁定规则是**角色自己的事**，不在这里表达。
        /// </summary>
        public float FacingYaw;

        /// <summary>这一 tick 按下了攻击（按下瞬间，不是按住）。</summary>
        public bool Attack;

        /// <summary>
        /// 这一 tick 按下了跳跃（按下瞬间，不是按住）。
        /// 跳不跳得起来由**角色**判断（要站在地上、不在攻击 / 硬直里）——命令只表达"想跳"。
        /// </summary>
        public bool Jump;

        /// <summary>
        /// **玩家路径**的解析器：把一帧设备输入 + 相机朝向翻译成角色意图。
        ///
        /// 它住在 Sim 而不是 Inputs/：需要的两样东西（<see cref="InputFrame"/>、相机 yaw）
        /// 都是 Sim 里的纯数据，这里只是数学——它不认识 Input System。
        /// （「朝向跟最近敌人」那种玩家侧规则，将来也加在这里。）
        ///
        /// AI 路径不用它：AI 直接填世界空间的 <see cref="MoveDirection"/>。
        /// </summary>
        public static ActorCommand FromPlayerInput(InputFrame frame, float cameraYaw)
        {
            ActorCommand command;
            command.MoveDirection = CameraRelativeDirection(frame.MoveX, frame.MoveY, cameraYaw);
            command.FacingYaw = cameraYaw;
            command.Attack = frame.Attack;
            command.Jump = frame.Jump;
            return command;
        }

        /// <summary>
        /// 把「相机空间的摇杆量」转成世界方向。（从 <c>PlayerSim</c> 搬来，只换了住处。）
        ///
        /// 相机偏航 θ 时，它的前方向量是 (sinθ, 0, cosθ)，右方向量是 (cosθ, 0, -sinθ)。
        /// 输入 (moveX, moveY) 是「右多少、前多少」，所以直接按这两个基向量合成。
        ///
        /// 结果长度会被限制在 1 以内：键盘斜按 W+D 会得到长度 √2，(1,1) 不能比单按 W 跑得快；
        /// 而摇杆推一半（长度 0.5）要保留——那是「慢走」的语义。
        /// </summary>
        public static Vector3 CameraRelativeDirection(float moveX, float moveY, float cameraYaw)
        {
            float rad = cameraYaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(rad);
            float cos = Mathf.Cos(rad);

            Vector3 forward = new Vector3(sin, 0f, cos);
            Vector3 right = new Vector3(cos, 0f, -sin);

            Vector3 direction = forward * moveY + right * moveX;

            // 只压长度、不改方向：saturate 而不是 normalize。
            float sqr = direction.sqrMagnitude;
            if (sqr > 1f)
            {
                direction /= Mathf.Sqrt(sqr);
            }

            return direction;
        }
    }
}

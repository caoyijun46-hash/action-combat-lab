using ActionGame.Sim;
using UnityEngine;

namespace ActionGame.Data
{
    /// <summary>
    /// **全局配置文件**（MV3a）：世界动力学 / 系统规则 / 削韧系数 / 相机与操作——
    /// 既不属于某一招、也不属于某一个角色的参数。全项目一份，Boot 持有。
    ///
    /// 播放中改这里**立刻生效**（Boot 每 tick 现读，和角色 / 招式资产一个待遇）。
    /// </summary>
    [CreateAssetMenu(fileName = "GlobalData", menuName = "ActionGame/Global Data")]
    public class GlobalDataAsset : ScriptableObject
    {
        [SerializeField] private GlobalData _data = new GlobalData
        {
            // 世界动力学：重力 30（游戏重力，不是 9.8）/ 冲量衰减 20 / 唯一地面 0
            Gravity = 30f,
            ImpulseDeceleration = 20f,
            GroundY = 0f,

            // 系统规则：取消窗口 3 tick / 预输入 12 tick / 慢放 120 真实 tick、每 2 跳 1
            AttackCancelWindowTicks = 3,
            AttackInputBufferTicks = 12,
            SlowMotionTicks = 120,
            SlowMotionSkipEvery = 2,

            // 削韧系数：等级差每级 0.5，夹在 [0.25, 2.0]
            PoiseCoefficientPerLevel = 0.5f,
            PoiseCoefficientMin = 0.25f,
            PoiseCoefficientMax = 2f,

            // 相机与操作：灵敏度 0.15°/像素 / 俯仰 ±89 / 鼠标转向上限 720°/s
            MouseSensitivityDegreesPerPixel = 0.15f,
            MaxPitchDegrees = 89f,
            CameraMaxTurnSpeedDegreesPerSecond = 720f,

            // 锁定：范围 10 米 / 增益 8 / 速度带 [90°, 360°]/s
            LockOnRangeMeters = 10f,
            LockOnTurnGainPerSecond = 8f,
            LockOnTurnMinSpeedDegreesPerSecond = 90f,
            LockOnTurnMaxSpeedDegreesPerSecond = 360f,

            // 输入判定：方向 ε 5° / 幅度 ε 0.05
            MoveInputDirectionEpsilonDegrees = 5f,
            MoveInputMagnitudeEpsilon = 0.05f,
        };

        /// <summary>
        /// 值拷贝交出去（和 <see cref="CharacterDataAsset.Data"/> 同款）。
        /// </summary>
        public GlobalData Data => _data;

        /// <summary>只警告不修正（同 AttackDataAsset 的纪律）。</summary>
        private void OnValidate()
        {
            // SkipEvery 是取模的分母：0 会直接炸（DivideByZeroException），负数语义不存在。
            if (_data.SlowMotionSkipEvery < 1)
            {
                Debug.LogWarning(
                    $"{name}: 慢放倍率是 {_data.SlowMotionSkipEvery}——必须是 ≥ 1 的整数" +
                    "（它是取模的分母，0 会直接崩）。", this);
            }

            // ---- 削韧系数（块 P1）----
            if (_data.PoiseCoefficientPerLevel < 0f)
            {
                Debug.LogWarning(
                    $"{name}: 削韧系数的每级变化是负数（{_data.PoiseCoefficientPerLevel}）——" +
                    "等级差会反向影响削韧，多半是手滑。", this);
            }

            if (_data.PoiseCoefficientMin < 0f)
            {
                Debug.LogWarning(
                    $"{name}: 削韧系数下限是负数（{_data.PoiseCoefficientMin}），运行时会按 0 处理。", this);
            }

            if (_data.PoiseCoefficientMax < _data.PoiseCoefficientMin)
            {
                Debug.LogWarning(
                    $"{name}: 削韧系数上限（{_data.PoiseCoefficientMax}）小于下限" +
                    $"（{_data.PoiseCoefficientMin}）——Clamp 的结果会不可靠。", this);
            }
        }
    }
}

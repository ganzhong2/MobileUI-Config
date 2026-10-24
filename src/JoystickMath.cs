using System;

namespace MobileUiConfig
{
    // 摇杆方向向量的共享计算：死区剔除、曲线变形、灵敏度缩放。
    // 编辑预览（PhonePreview）与真机悬浮层（OverlayJoystickForm）共用同一口径。
    internal static class JoystickMath
    {
        // 输入 dx/dy 为拖动偏移与行程归一化后的向量（长度上限 1），
        // 输出 nx/ny 为经过死区、曲线、灵敏度处理后的最终方向向量，
        // length 为处理后的归一化长度（0～1），供高亮/力度判断使用。
        public static void Apply(JoystickConfig joy, float dx, float dy,
            out float nx, out float ny, out float length)
        {
            nx = 0f;
            ny = 0f;
            length = 0f;
            if (joy == null) return;

            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001f) return;

            // 死区半径占外圈半径百分比；区内视为零输入。
            float deadZone = joy.DeadZone / 100f;
            if (len <= deadZone) return;

            float ux = dx / len;
            float uy = dy / len;

            // 死区重映射到 [0,1]：剔除死区后剩余行程重新拉满。
            float t = (len - deadZone) / Math.Max(0.0001f, 1f - deadZone);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            t = Curve(joy.CurveType, t, joy.CurveStrength);

            // 灵敏度：100 为原始强度，50 减半、200 翻倍，输出封顶 1。
            t = t * (joy.Sensitivity / 100f);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            nx = ux * t;
            ny = uy * t;
            length = t;
        }

        // 曲线变形：按类型取目标曲线，再按弯曲度与线性结果混合。
        public static float Curve(int type, float t, int strength)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float k = strength / 100f;
            float target;
            switch (type)
            {
                case 1:
                    // 加速（凸）：起步慢、推满快。
                    target = t * t;
                    break;
                case 2:
                    // 减速（凹）：起步快、到顶慢。
                    target = (float)Math.Sqrt(t);
                    break;
                case 3:
                    // S 型：中段陡、两端缓。
                    target = 3f * t * t - 2f * t * t * t;
                    break;
                default:
                    // 线性。
                    target = t;
                    break;
            }
            return t * (1f - k) + target * k;
        }

        public static string CurveName(int type)
        {
            switch (type)
            {
                case 1: return "加速";
                case 2: return "减速";
                case 3: return "S 型";
                default: return "线性";
            }
        }
    }
}

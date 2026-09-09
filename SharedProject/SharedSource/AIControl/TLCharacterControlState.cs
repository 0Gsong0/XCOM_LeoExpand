using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace XCOM_LeoExpand
{
    public enum TLCharacterAiMode
    {
        Vanilla,// 完全交还原版
        MoveTo,// 寻路前往目标点
        Guard,// 驻守，保留原版索敌和攻击，限制离开锚点
        Disabled// 禁用 AI，清除移动和攻击输入
    }
    /// <summary>
    /// 生物控制状态实例
    /// </summary>
    public class TLCharacterControlState
    {
        public ushort CharacterId { get; set; }
        public ushort GuardWaypointId { get; set; } = Entity.NullEntityID;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TLCharacterAiMode Mode { get; set; }

        public ushort TargetSubId { get; set; }
        public float TargetLocalX { get; set; }
        public float TargetLocalY { get; set; }

        /// <summary>
        /// 抵达 MoveTo 目标的判定距离，显示单位。
        /// </summary>
        public float ArrivalDistance { get; set; } = 100.0f;

        /// <summary>
        /// Guard 模式允许离开锚点的距离，显示单位。
        /// </summary>
        public float GuardRadius { get; set; } = 100.0f;

        /// <summary>
        /// 移动计时器
        /// </summary>
        [JsonIgnore]
        public float MoveToElapsedTime { get; set; }

        /// <summary>
        /// 创建时还在等待角色进入某个 Hull。
        /// 不需要同步。
        /// </summary>
        [JsonIgnore]
        public bool WaitingForInteriorAnchor { get; set; }

        [JsonIgnore]
        public Vector2 TargetLocalPosition
        {
            get => new(TargetLocalX, TargetLocalY);
            set
            {
                TargetLocalX = value.X;
                TargetLocalY = value.Y;
            }
        }

        public TLCharacterControlState Clone()
        {
            return new TLCharacterControlState
            {
                CharacterId = CharacterId,
                Mode = Mode,
                TargetSubId = TargetSubId,
                TargetLocalX = TargetLocalX,
                TargetLocalY = TargetLocalY,
                ArrivalDistance = ArrivalDistance,
                GuardRadius = GuardRadius,
                WaitingForInteriorAnchor = WaitingForInteriorAnchor,
                GuardWaypointId = GuardWaypointId
            };
        }
    }
}

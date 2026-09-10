using FarseerPhysics;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace XCOM_LeoExpand
{
    public static class TLCharacterControlSystem
    {
        private static readonly Identifier GuardPositionTag = "GuardPos".ToIdentifier();
        //20秒超时
        private const float MoveToTeleportDelay = 20.0f;
        //10米搜索范围
        private const float FindGuardPosDis = 10f;

        private static readonly Dictionary<ushort, TLCharacterControlState> states = new();

        public static IReadOnlyDictionary<ushort, TLCharacterControlState> States => states;

        private static readonly Dictionary<Identifier, TLCharacterAiMode> autoControlledCharacters = new()
        {
            ["Tmr-01驮兽".ToIdentifier()] = TLCharacterAiMode.Guard,
            ["XCOM_Muton".ToIdentifier()] = TLCharacterAiMode.Guard,
            ["XCOM_Thinman".ToIdentifier()] = TLCharacterAiMode.Guard,
        };
        public static void Init(Harmony harmony)
        {
            var characterCreat = AccessTools.Method(typeof(Character), nameof(Character.Create), new[]
            {
                typeof(CharacterPrefab),
                typeof(Vector2),
                typeof(string),
                typeof(CharacterInfo),
                typeof(ushort),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(RagdollParams),
                typeof(bool)
            });
            var characterRemove = AccessTools.Method(typeof(Character), nameof(Character.Remove));
            var enemyAi = AccessTools.Method(typeof(EnemyAIController), nameof(EnemyAIController.Update));
            if(characterCreat != null)
            {
                harmony.Patch(characterCreat, postfix: new HarmonyMethod(typeof(TLCharacterControlSystem), nameof(OnCharacterCreated)));
            }
            if(characterRemove != null)
            {
                harmony.Patch(characterRemove, postfix: new HarmonyMethod(typeof(TLCharacterControlSystem), nameof(OnCharacterRemoved)));
            }
            if(enemyAi != null)
            {
                harmony.Patch(enemyAi, prefix: new HarmonyMethod(typeof(TLCharacterControlSystem), nameof(BeforeEnemyAIUpdate)),
                    postfix:new HarmonyMethod(typeof(TLCharacterControlSystem), nameof(AfterEnemyAIUpdate)));
            }
        }
        private static void OnCharacterCreated(Character? __result)
        {
            if (__result == null || __result.Removed) return;
            if (!IsAuthority) return;
            if (__result.AIController is not EnemyAIController) return;
            if (!autoControlledCharacters.TryGetValue(__result.Prefab.Identifier, out TLCharacterAiMode mode)) return;
            Register(__result, mode);
        }
        private static void OnCharacterRemoved(Character __instance)
        {
            if (__instance == null) return;
            bool removed = states.Remove(__instance.ID);
            if (removed && IsAuthority)
            {
                //快照同步
            }
        }
        private static bool BeforeEnemyAIUpdate(EnemyAIController __instance, float deltaTime)
        {
            if (!IsAuthority) return true;
            Character character = __instance.Character;
            if(!TryGetActiveInteriorState(__instance.Character, out TLCharacterControlState? state))return true;
            switch (state.Mode)
            {
                case TLCharacterAiMode.Vanilla:
                    return true;
                case TLCharacterAiMode.Disabled:
                    StopMovementOnly(character, __instance);
                    return false;
                case TLCharacterAiMode.MoveTo:
                    /*
                     * EnemyAIController 初始使用的是普通 SteeringManager。
                     * 必须至少允许原版执行一次，让它根据
                     * Character.Submarine 和 UsePathFinding
                     * 切换成 IndoorsSteeringManager。
                     */
                    if (__instance.SteeringManager is not IndoorsSteeringManager)
                    {
                        return true;
                    }
                    // 已经切换到室内寻路，禁止原版继续抢控制权。
                    return false;
                case TLCharacterAiMode.Guard:
                    /*
                     * 在防守点附近：
                     * 允许原版选择敌人和发动攻击。
                     *
                     * 离开防守范围：
                     * 禁止原版追击，由 UpdateGuard 拉回去。
                     */
                    Vector2 targetWorldPosition = GetTargetWorldPosition(state);
                    float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, targetWorldPosition);
                    bool insideGuardRadius = distanceSquared <= state.GuardRadius * state.GuardRadius;
                    return insideGuardRadius;
                default: return true;
            }
        }
        private static void AfterEnemyAIUpdate(EnemyAIController __instance, float deltaTime)
        {
            if (!IsAuthority) return;
            Character character = __instance.Character;
            if (!TryGetActiveInteriorState(__instance.Character, out TLCharacterControlState? state)) return;
            switch (state.Mode)
            {
                case TLCharacterAiMode.Vanilla:
                    return;
                case TLCharacterAiMode.MoveTo:
                    UpdateMoveTo(character, __instance, state, deltaTime);
                    break;
                case TLCharacterAiMode.Guard:
                    UpdateGuard(character, __instance, state, deltaTime);
                    break;
                case TLCharacterAiMode.Disabled:
                    // Prefix 已经处理。
                    StopMovementOnly(character, __instance);
                    break;
            }
        }
        private static void UpdateMoveTo(Character character, EnemyAIController ai, TLCharacterControlState state, float deltaTime)
        {
            Vector2 targetWorldPosition = GetTargetWorldPosition(state);
            float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, targetWorldPosition);
            if(distanceSquared <= state.ArrivalDistance * state.ArrivalDistance)
            {
                //到达后转为 Guard。目标位置继续作为驻守锚点。
                state.Mode = TLCharacterAiMode.Guard;
                state.MoveToElapsedTime = 0.0f;
                StopMovementOnly(character, ai);
                //快照同步
                return;
            }
            character.ClearInputs();
            ai.SteeringManager.Reset();
            if (ai.SteeringManager is not IndoorsSteeringManager indoors)
            {
                //角色虽然处于 Hull 中，但暂未切换成室内寻路器。本帧先不接管。
                character.AnimController.TargetMovement = Vector2.Zero;
                return;
            }

            state.MoveToElapsedTime += deltaTime;
            if(state.MoveToElapsedTime >= MoveToTeleportDelay)
            {
                TeleportToGuardPosition(ai, character, state);
                return;
            }

            Vector2 targetSimPosition = ConvertUnits.ToSimUnits(state.TargetLocalPosition);
            indoors.SteeringSeek(targetSimPosition, weight: 10.0f, nodeFilter: node => node.Waypoint.Submarine != null);
            ai.SteeringManager.Update(character.AnimController.GetCurrentSpeed(true));
        }
        private static void UpdateGuard(Character character,EnemyAIController ai,TLCharacterControlState state,float deltaTime)
        {
            Vector2 targetWorldPosition = GetTargetWorldPosition(state);
            Vector2 targetSimPosition = ConvertUnits.ToSimUnits(state.TargetLocalPosition);
            float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, targetWorldPosition);
            float guardRadiusSquared = state.GuardRadius * state.GuardRadius;
            if (distanceSquared <= guardRadiusSquared)
            {
                //位于驻守范围内,保留原版攻击输入，只消除移动。
                StopMovementOnly(character, ai);
                FaceSelectedTarget(character, ai);
                return;
            }
            state.MoveToElapsedTime = 0.0f;
            state.Mode = TLCharacterAiMode.MoveTo;
        }
        private static void TeleportToGuardPosition(EnemyAIController ai, Character character, TLCharacterControlState state)
        {
            if (Entity.FindEntityByID(state.GuardWaypointId) is not WayPoint guardPoint || guardPoint.Removed || guardPoint.CurrentHull == null || guardPoint.Submarine != character.Submarine)
            {
                //防守点已经失效,交还原版
                state.MoveToElapsedTime = 0.0f;
                state.Mode = TLCharacterAiMode.Vanilla;
                return;
            }
            ai.SteeringManager.Reset();
            if (ai.SteeringManager is IndoorsSteeringManager indoors)
            {
                indoors.ResetPath();
            }

            character.TeleportTo(guardPoint.WorldPosition);

            character.AnimController.Collider.ResetDynamics();
            character.AnimController.BodyInRest = false;
            state.MoveToElapsedTime = 0.0f;
            state.Mode = TLCharacterAiMode.Guard;
            StopMovementOnly(character, ai);
        }
        private static void FaceSelectedTarget(Character character, EnemyAIController ai)
        {
            Entity? target = ai.SelectedAiTarget?.Entity;
            if (target == null || target.Removed) return;
            float horizontalDifference = target.WorldPosition.X - character.WorldPosition.X;
            if (Math.Abs(horizontalDifference) < 1.0f) return;
            ai.FaceTarget(target);
        }
        private static bool IsAuthority => GameMain.NetworkMember is not { IsClient: true };
        /// <summary>
        /// 注册到接管AI更新表
        /// </summary>
        private static bool Register(Character character, TLCharacterAiMode mode = TLCharacterAiMode.Guard,float guardRadius = 100.0f)
        {
            if (!IsAuthority) return false;
            if (!CanControl(character)) return false;
            TLCharacterControlState state = new()
            {
                CharacterId = character.ID,
                Mode = mode,
                GuardRadius = Math.Max(guardRadius, 20.0f)
            };
            
            InitializeTargetPosition(character, state);

            states[character.ID] = state;
            //快照同步
            return true;
        }
        private static bool CanControl(Character character)
        {
            return character != null && !character.Removed && !character.IsDead && character.AIController is EnemyAIController;
        }
        /// <summary>
        /// 延迟初始化
        /// 这个角色当前是否应该由我的室内 AI 系统接管
        /// </summary>
        /// <param name="character"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private static bool TryGetActiveInteriorState(Character character, out TLCharacterControlState? state)
        {
            state = null;
            if (!CanControl(character)) return false;
            if (!states.TryGetValue(character.ID, out state)) return false;
            if (character.CurrentHull == null || character.Submarine == null) return false;
            if(state.WaitingForInteriorAnchor || state.TargetSubId == Entity.NullEntityID)
            {
                InitializeTargetPosition(character, state);
                if (IsAuthority)
                {
                    //快照同步
                }
            }
            if (character.Submarine.ID != state.TargetSubId) return false;
            return state.Mode != TLCharacterAiMode.Vanilla;
        }
        public static bool TryGetState(ushort characterId, out TLCharacterControlState? state)
        {
            return states.TryGetValue(characterId, out state);
        }
        public static Vector2 GetTargetWorldPosition(TLCharacterControlState state)
        {
            Entity? entity = Entity.FindEntityByID(state.TargetSubId);
            if (entity is Submarine submarine)
            {
                return submarine.Position +
                       state.TargetLocalPosition;
            }
            return state.TargetLocalPosition;
        }
        private static void StopMovementOnly(Character character, AIController ai)
        {
            ai.SteeringManager.Reset();
            character.AnimController.TargetMovement = Vector2.Zero;
            character.SetInput(InputType.Left, false, false);
            character.SetInput(InputType.Right, false, false);
            character.SetInput(InputType.Up, false, false);
            character.SetInput(InputType.Down, false, false);
        }
        /// <summary>
        /// 统一初始化目标
        /// </summary>
        /// <param name="character"></param>
        /// <param name="state"></param>
        private static void InitializeTargetPosition(Character character,TLCharacterControlState state)
        {
            if (character.CurrentHull == null || character.Submarine == null)
            {
                state.TargetSubId = Entity.NullEntityID;
                state.WaitingForInteriorAnchor = true;
                return;
            }
            if (TryAssignRandomGuardPosition(character, state))
            {
                return;
            }
            DebugConsole.NewMessage(
               $"[TL AI 接管] {character.Name} " +
               "附近没有空闲防守点，归还原版 AI。",
               Color.Orange);
            ReturnToVanilla(character, state);
        }
        private static void ReturnToVanilla(Character character,TLCharacterControlState state)
        {
            state.Mode = TLCharacterAiMode.Vanilla;
            state.TargetSubId = character.Submarine?.ID ?? Entity.NullEntityID;
            state.TargetLocalPosition = Vector2.Zero;
            state.GuardWaypointId = Entity.NullEntityID;
            state.MoveToElapsedTime = 0.0f;
            state.WaitingForInteriorAnchor = false;
        }
        private static List<WayPoint> FindGuardPositions(Character character)
        {
            if (character.Submarine == null)
            {
                return new List<WayPoint>();
            }
            float searchRadius = ConvertUnits.ToDisplayUnits(FindGuardPosDis);
            float searchRadiusSquared = searchRadius * searchRadius;

            return WayPoint.WayPointList.Where(wayPoint =>
                wayPoint != null &&
                wayPoint.Removed == false &&
                wayPoint.CurrentHull != null &&
                wayPoint.Submarine == character.Submarine &&
                wayPoint.Tags.Contains(GuardPositionTag) &&
                Vector2.DistanceSquared(character.WorldPosition, wayPoint.WorldPosition) <= searchRadiusSquared).ToList();
        }
        /// <summary>
        /// 随机分配防守点
        /// 优先选择没有被其他有效角色占用的点；
        /// 如果全部被占用，则允许重复分配。
        /// </summary>
        private static bool TryAssignRandomGuardPosition(Character character,TLCharacterControlState state)
        {
            if (character.CurrentHull == null || character.Submarine == null) return false;
            List<WayPoint> guardPositions = FindGuardPositions(character);
            if (guardPositions.Count == 0) return false;

            List<WayPoint> unoccupiedPositions = guardPositions.Where(wayPoint =>
                !IsGuardPositionOccupied(wayPoint, character.ID)).ToList();

            if (unoccupiedPositions.Count == 0) return false;

            //尚有空闲点：只在空闲点中随机。
            //全部占用：退回所有点中随机，允许重复。

            WayPoint? selected = unoccupiedPositions.GetRandomUnsynced();
            state.TargetSubId = character.Submarine.ID;
            state.TargetLocalPosition = selected.Position;
            state.GuardWaypointId = selected.ID;
            state.MoveToElapsedTime = 0.0f;
            state.WaitingForInteriorAnchor = false;

            float distanceSquared = Vector2.DistanceSquared(character.WorldPosition, selected.WorldPosition);
            if(state.Mode == TLCharacterAiMode.Guard)
            {
                state.Mode = distanceSquared <= state.ArrivalDistance * state.ArrivalDistance ? TLCharacterAiMode.Guard : TLCharacterAiMode.MoveTo;
            }
            string allocationType =
                unoccupiedPositions.Count > 0
                    ? "空闲防守点"
                    : "重复防守点（所有点均已占用）";

            DebugConsole.NewMessage(
                $"[TL AI 接管] {character.Name} " +
                $"分配{allocationType} ,防守点: {selected.ID}，" +
                $"局部坐标：{selected.Position}",
                Color.LightGreen);
            return true;
        }
        /// <summary>
        /// 判断一个防守点是否已经被有效角色占用
        /// </summary>
        /// <returns></returns>
        private static bool IsGuardPositionOccupied(WayPoint guardPosition, ushort exceptCharacterId = Entity.NullEntityID)
        {
            foreach (TLCharacterControlState otherState in states.Values)
            {
                // 重新给某个角色分配时，不把它自己的旧分配算进去。
                if (otherState.CharacterId == exceptCharacterId) continue;
                if (otherState.Mode == TLCharacterAiMode.Vanilla) continue;
                if (otherState.WaitingForInteriorAnchor) continue;
                if (otherState.TargetSubId != guardPosition.Submarine?.ID) continue;
                if (otherState.GuardWaypointId != guardPosition.ID) continue;

                if (Entity.FindEntityByID(otherState.CharacterId) is not Character assignedCharacter) continue;
                if (assignedCharacter.Removed || assignedCharacter.IsDead) continue;
                return true;
            }
            return false;
        }
    }
}

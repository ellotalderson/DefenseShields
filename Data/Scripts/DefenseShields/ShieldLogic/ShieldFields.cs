﻿
using VRage.Voxels;

namespace DefenseShields
{
    using VRage.Game.ModAPI;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Support;
    using Sandbox.Game.Entities;
    using Sandbox.Game.EntityComponents;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.Entity;
    using VRageMath;

    public partial class DefenseShields 
    {
        #region Setup
        internal readonly MyDefinitionId GId = MyResourceDistributorComponent.ElectricityId;

        internal readonly Dictionary<int, float> ExpChargeReductions = new Dictionary<int, float>
        {
            {0, 1f},
            {1, 1.5f}, 
            {2, 2f}, 
            {3, 2.5f}, 
            {4, 3f}, 
            {5, 3.5f}, 
            {6, 4f}, 
            {7, 4.5f},
            {8, 5f}, 
            {9, 5.5f}, 
            {10, 6f},
        };
        internal readonly float[] ReserveScaler = { -1f, 0.001f, 1, 1000, 1000000 };

        internal readonly List<MyEntity> PruneList = new List<MyEntity>();
        internal readonly List<ShieldHit> ShieldHits = new List<ShieldHit>();
        internal readonly Queue<ShieldHitValues> ShieldHitsToSend = new Queue<ShieldHitValues>();

        internal readonly HashSet<MyEntity> AuthenticatedCache = new HashSet<MyEntity>();
        internal readonly HashSet<MyEntity> IgnoreCache = new HashSet<MyEntity>();
        internal readonly HashSet<MyEntity> EntityBypass = new HashSet<MyEntity>();
        internal readonly HashSet<MyEntity> EnemyShields = new HashSet<MyEntity>();
        internal readonly HashSet<MyEntity> FriendlyMissileCache = new HashSet<MyEntity>();
        internal readonly ShieldChargeMgr ChargeMgr = new ShieldChargeMgr();
        internal readonly ConcurrentDictionary<MyEntity, ProtectCache> ProtectedEntCache = new ConcurrentDictionary<MyEntity, ProtectCache>();
        internal readonly MyShipController FakeController = new MyShipController();

        internal readonly ConcurrentDictionary<MyEntity, EntIntersectInfo> WebEnts = new ConcurrentDictionary<MyEntity, EntIntersectInfo>();
        internal readonly ConcurrentDictionary<MyEntity, MoverInfo> EntsByMe = new ConcurrentDictionary<MyEntity, MoverInfo>();
        internal readonly ConcurrentDictionary<MyVoxelBase, int> VoxelsToIntersect = new ConcurrentDictionary<MyVoxelBase, int>();
        internal readonly Dictionary<Session.ShieldSides, Session.ShieldInfo> RealSideStates = new Dictionary<Session.ShieldSides, Session.ShieldInfo> 
        {
            {Session.ShieldSides.Left, new Session.ShieldInfo() },
            {Session.ShieldSides.Right, new Session.ShieldInfo() },
            {Session.ShieldSides.Up, new Session.ShieldInfo() },
            {Session.ShieldSides.Down, new Session.ShieldInfo() },
            {Session.ShieldSides.Forward, new Session.ShieldInfo() },
            {Session.ShieldSides.Backward, new Session.ShieldInfo() }
        };

        internal readonly Dictionary<Session.ShieldSides, bool> RenderingSides = new Dictionary<Session.ShieldSides, bool>
        {
            {Session.ShieldSides.Left, false },
            {Session.ShieldSides.Right, false },
            {Session.ShieldSides.Up, false },
            {Session.ShieldSides.Down, false },
            {Session.ShieldSides.Forward, false },
            {Session.ShieldSides.Backward, false }
        };
        internal const int ConvToHp = 100;
        internal const float ConvToDec = 0.01f;

        internal volatile int LogicSlot;
        internal volatile int MonitorSlot;
        internal volatile bool MoverByShield;
        internal volatile bool PlayerByShield;
        internal volatile bool NewEntByShield;
        internal volatile bool Asleep;
        internal volatile bool WasPaused;
        internal volatile uint LastWokenTick;
        internal volatile bool ReInforcedShield;

        internal int LostPings;
        internal Vector3I ShieldRedirectState;

        internal BoundingBoxD ConstructAaab;
        internal BoundingBoxD WebBox = new BoundingBoxD();
        internal MatrixD OldShieldMatrix;
        internal ShieldGridComponent ShieldComp;
        internal BoundingBoxD ShieldBox3K = new BoundingBoxD();
        internal MyOrientedBoundingBoxD SOriBBoxD = new MyOrientedBoundingBoxD();
        internal BoundingSphereD ShieldSphere = new BoundingSphereD(Vector3D.Zero, 1);
        internal BoundingBox ShieldAabbScaled = new BoundingBox(Vector3D.One, -Vector3D.One);
        internal BoundingSphereD ShieldSphere3K = new BoundingSphereD(Vector3D.Zero, 1f);
        internal BoundingSphereD WebSphere = new BoundingSphereD(Vector3D.Zero, 1f);
        internal MyStorageData TmpStorage = new MyStorageData();
        internal MyEntity ShellActive;
        internal MyCockpit LastCockpit;
        internal bool InControlPanel => MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.ControlPanel;
        internal bool InThisTerminal => Session.Instance.LastTerminalId == Shield.EntityId;
       
        private const int ReModulationCount = 300;
        private const int PowerNoticeCount = 600;
        private const int CapacitorDrainCount = 60;
        private const int CapacitorStableCount = 600;
        private const int OverHeat = 600;
        private const int HeatingStep = 600;
        private const int CoolingStep = 1200;
        private const int FallBackStep = 10;
        private const float MagicEllipsoidRatio = 1000;
        private const float BlockDensityLimit = 0.05f;
        private const float PowerDensityLimit = 0.125f;
        private const int SinkCountTime = 20;

        private const int SyncCount = 60;

        private const string SpaceWolf = "Space_Wolf";
        private const string ModelMediumReflective = "\\Models\\Cubes\\ShieldPassive11.mwm";
        private const string ModelHighReflective = "\\Models\\Cubes\\ShieldPassive.mwm";
        private const string ModelLowReflective = "\\Models\\Cubes\\ShieldPassive10.mwm";
        private const string ModelRed = "\\Models\\Cubes\\ShieldPassive09.mwm";
        private const string ModelBlue = "\\Models\\Cubes\\ShieldPassive08.mwm";
        private const string ModelGreen = "\\Models\\Cubes\\ShieldPassive07.mwm";
        private const string ModelPurple = "\\Models\\Cubes\\ShieldPassive06.mwm";
        private const string ModelGold = "\\Models\\Cubes\\ShieldPassive05.mwm";
        private const string ModelOrange = "\\Models\\Cubes\\ShieldPassive04.mwm";
        private const string ModelCyan = "\\Models\\Cubes\\ShieldPassive03.mwm";
        private const string ModelDirty = "\\Models\\Cubes\\Large\\StationShield.mwm";

        private readonly RunningAverage _dpsAvg = new RunningAverage(2);
        private readonly RunningAverage _hpsAvg = new RunningAverage(2);
        private readonly EllipsoidOxygenProvider _ellipsoidOxyProvider = new EllipsoidOxygenProvider(Matrix.Zero);
        private readonly EllipsoidSA _ellipsoidSa = new EllipsoidSA(double.MinValue, double.MinValue, double.MinValue);
        private readonly Vector3D[] _resetEntCorners = new Vector3D[8];
        private readonly Vector3D[] _obbCorners = new Vector3D[8];
        private readonly Vector3D[] _obbPoints = new Vector3D[9];
        private readonly Vector3D[] _triEdges = new Vector3D[3];
        private readonly Vector3D[] _obbEdges = new Vector3D[3];
        private uint _tick;
        private uint _subTick;
        private uint _funcTick;
        private uint _shapeTick;
        private uint _capacitorTick;
        private uint _delayedCapTick = uint.MaxValue;
        private uint _heatVentingTick = uint.MaxValue;
        private uint _lastSendDamageTick = uint.MaxValue;
        private uint _subUpdatedTick = uint.MaxValue;
        private uint _maxPowerTick;
        private uint _lastHeatTick;
        private float _power = 0.001f;
        private float _powerNeeded;
        private float _otherPower;
        private float _batteryCurrentInput;
        internal float ShieldPeakRate;
        internal float ShieldMaxChargeRate;
        private float _damageReadOut;
        private float _shieldMaintaintPower;
        private float _shieldConsumptionRate;
        private float _oldShieldFudge;
        private float _heatScaleHp = 1f;
        private float _runningDamage;
        private float _runningHeal;
        private float _sizeScaler;
        private float _shieldTypeRatio = 100f;
        internal float ExpChargeReduction;
        private double _oldEllipsoidAdjust;
        private double _ellipsoidSurfaceArea;
        
        private int _sinkCount;

        private int _overChargeCount;
        private int _linkedGridCount = -1;
        private int _count = -1;
        private int _powerNoticeLoop;
        private int _capacitorLoop;
        private int _overLoadLoop = -1;
        private int _reModulationLoop = -1;
        private int _heatCycle = -1;
        private int _fallbackCycle;
        private int _currentHeatStep;
        private int _heatScaleTime = 1;
        private int _prevLod;
        private int _onCount;
        private int _bCount;
        private int _bTime;
        private int _pLossTimer;
        private bool _bInit;
        private int _clientMessageCount;
        private long _gridOwnerId = -1;
        private long _controllerOwnerId = -1;
        internal bool AutoManage;
        internal bool StateChangeRequest;
        private bool _firstLoop = true;
        private bool _enablePhysics = true;
        private bool _shieldCapped;
        private bool _needPhysics;
        private bool _allInited;
        private bool _containerInited;
        private bool _comingOnline;
        private bool _tick20;
        private bool _tick30;
        private bool _tick60;
        private bool _tick180;
        private bool _tick300;
        private bool _tick600;
        private bool _tick1800;
        private bool _resetEntity;
        private bool _isDedicated;
        private bool _mpActive;
        private bool _isServer;
        private bool _shieldPowered;
        private bool _subUpdate;
        private bool _hideShield;
        private bool _hideColor;
        private bool _supressedColor;
        private bool _shapeChanged;
        private bool _entityChanged;
        private bool _updateRender;
		private bool _functionalAdded;
        private bool _functionalRemoved;
        private bool _blockAdded;
        private bool _blockChanged;
		private bool _blockEvent;
        private bool _shapeEvent;
        private bool _updateMobileShape;
        private bool _clientNotReady;
        private bool _clientAltered;
        private bool _clientOn;
        private bool _viewInShield;
        private bool _powerFail;
        private bool _halfExtentsChanged;
        private bool _readyToSync;
        private bool _firstSync;
        private bool _adjustShape;
        private bool _updateCap;
        private bool _sendMessage;
        private bool _checkResourceDist;
        private const string ModelActive = "\\Models\\Cubes\\ShieldActiveBaseAlt.mwm";
        private string _modelPassive = string.Empty;

        private Vector3D _localImpactPosition;
        private Vector3D _oldGridHalfExtents;
        internal uint RedirectUpdateTime;
        internal Quaternion SQuaternion;
        internal int LastIndex;
        internal long LastAttackerId = -1;
        internal double IncreaseO2ByFPercent;
        internal bool EwarProtection;
        internal readonly float[] AttackerDamage = new float[100];
        internal readonly uint[] AttackerTimes = new uint[100];
        internal readonly Queue<long> AttackerLast = new Queue<long>(100);
        internal readonly Dictionary<long, int> AttackerLookupCache = new Dictionary<long, int>();

        private MyResourceSinkInfo _resourceInfo;
        private MyResourceSinkComponent _sink;

        private MyCubeGrid _slavedToGrid;
        private MyEntity _shellPassive;
        private MyEntity3DSoundEmitter _alertAudio;
        private MySoundPair _audioReInit;
        private MySoundPair _audioSolidBody;
        private MySoundPair _audioOverload;
        private MySoundPair _audioRemod;
        private MySoundPair _audioLos;
        private MySoundPair _audioNoPower;

        private DSUtils Dsutil1 { get; set; } = new DSUtils();
        #endregion

        public enum Ent
        {
            Ignore,
            Protected,
            Friendly,
            EnemyPlayer,
            EnemyInside,
            NobodyGrid,
            EnemyGrid,
            Shielded,
            Other,
            VoxelBase,
            Authenticated,
            Floater
        }

        internal enum State
        {
            Active,
            Failure,
            Init,
            Lowered,
            Sleep,
            Wake
        }

        internal enum ShieldType
        {
            Station,
            LargeGrid,
            SmallGrid,
            Unknown
        }

        public enum PlayerNotice
        {
            EmitterInit,
            FieldBlocked,
            OverLoad,
            Remodulate,
            NoPower,
            NoLos
        }

        public int HitCoolDown { get; private set; } = -11;
        public int DtreeProxyId { get; set; } = -1;

        internal IMyUpgradeModule Shield { get; set; }
        internal ShieldType ShieldMode { get; set; }
        internal MyCubeGrid MyGrid;
        internal MyCubeBlock MyCube { get; set; }
        internal MyEntity ShieldEnt { get; set; }

        internal MyResourceDistributorComponent MyResourceDist { get; set; }

        internal ControllerSettings DsSet { get; set; }
        internal ControllerState DsState { get; set; }
        internal ShieldHitValues ShieldHit { get; set; } = new ShieldHitValues();
        internal Icosphere.Instance Icosphere { get; set; }


        internal uint ResetEntityTick { get; set; }
        internal uint LosCheckTick { get; set; }
        internal uint TicksWithNoActivity { get; set; }
        internal uint EffectsCleanTick { get; set; }
        internal uint InitTick { get; set; }
        internal uint ShapeChangeTick { get; set; }
        internal uint ClientHeatSinkResetTick { get; set; }
        internal uint LastModulateChangeTick { get; set; }
        internal float ShieldChargeRate { get; set; }
        internal float ShieldMaxCharge { get; set; }
        internal float GridMaxPower { get; set; }
        internal float GridCurrentPower { get; set; }
        internal float GridAvailablePower { get; set; }
        internal float ShieldCurrentPower { get; set; }
        internal float ShieldAvailablePower { get; set; }
        internal float ShieldMaxPower { get; set; }
        internal float ShieldMaxHp { get; set; }
        internal float ShieldChargeBase { get; set; }
        internal double BoundingRange { get; set; }
        internal bool AggregateModulation { get; set; }

        internal bool NotFailed { get; set; }
        internal bool DeformEnabled { get; set; }
        internal bool WarmedUp { get; set; }
        internal bool Warming { get; set; }
        internal bool UpdateDimensions { get; set; }
        internal bool FitChanged { get; set; }
        internal bool GridIsMobile { get; set; }
        internal bool SettingsUpdated { get; set; }
        internal bool SettingsChangeRequest { get; set; }
        internal bool IsStatic { get; set; }
        internal bool EntCleanUpTime { get; set; }
        internal bool ShieldActive { get; set; }
        internal bool ClientInitPacket { get; set; }

        internal Vector3D MyGridCenter { get; set; }
        internal Vector3D WorldEllipsoidCenter { get; set; }

        internal MatrixD DetectMatrixOutsideInv;
        internal MatrixD ShieldShapeMatrix { get; set; }
        internal MatrixD DetectMatrixOutside { get; set; }
        internal MatrixD ShieldMatrix { get; set; }

        internal MatrixD OffsetEmitterWMatrix { get; set; }


        internal DamageHandlerHit HandlerImpact { get; set; } = new DamageHandlerHit();
        internal Vector3D ShieldSize;

        internal int HeatSinkCount;

        internal enum HitType
        {
            Energy,
            Kinetic,
        }

        internal MatrixD DetectionMatrix
        {
            get
            {
                return DetectMatrixOutside;
            }

            set
            {
                DetectMatrixOutside = value;
                DetectMatrixOutsideInv = MatrixD.Invert(value);
            }
        }
    }
}

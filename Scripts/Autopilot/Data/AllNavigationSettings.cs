using System;
using Rynchodon.Autopilot.Navigator;
using Rynchodon.Settings;
using Rynchodon.Utility.Vectors;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon.Autopilot.Data
{
	/// <summary>
	/// <para>Separates navigation settings into distinct levels.</para>
	/// </summary>
	public class AllNavigationSettings
	{

		public enum SettingsLevelName : byte { Commands, NavRot, NavMove, NavEngage, NavWay, Current = NavWay };

		public class SettingsLevel : IDisposable
		{
			private SettingsLevel parent;

			private PseudoBlock m_navigationBlock;
			private PseudoBlock m_landingBlock;

			private EnemyFinder m_enemyFinder;
			private INavigatorMover m_navigatorMover;
			private INavigatorRotator m_navigatorRotator;
			private InfoString.StringId m_complaint;
			private BlockNameOrientation m_destBlock;
			private IMyEntity m_destEntity;
			private Func<IMyEntity, bool> m_ignoreEntity;

			private TimeSpan? m_waitUntil;

			private PositionBlock? m_destinationOffset;

			private float? m_destRadius, m_distance, m_distanceAngle, m_speedTarget, m_speedMaxRelative, m_minDistToJump;

			private bool? m_ignoreAsteroid, m_pathfindeCanChangeCourse, m_formation;

			/// <summary>
			/// Creates the top-level SettingLevel, which has defaults set.
			/// </summary>
			internal SettingsLevel(IMyCubeBlock NavBlock)
			{
				m_navigationBlock = new PseudoBlock(NavBlock);

				m_waitUntil = Globals.ElapsedTime.Add(new TimeSpan(0, 0, 1));

				m_destinationOffset = new PositionBlock() { vector = Vector3D.Zero };

				m_destRadius = DefaultRadius;
				m_distance = float.NaN;
				m_distanceAngle = float.NaN;
				m_speedTarget = DefaultSpeed;
				m_speedMaxRelative = float.MaxValue;
				m_minDistToJump = 0f;

				m_ignoreAsteroid = false;
				m_pathfindeCanChangeCourse = true;
				m_formation = false;
			}

			/// <summary>
			/// Creates a SettingLevel with a parent. Where values are not present, value from parent will be used.
			/// </summary>
			internal SettingsLevel(SettingsLevel parent)
			{ this.parent = parent; }

			public void Dispose()
			{
				IDisposable disposable = m_navigatorMover as IDisposable;
				if (disposable != null)
					disposable.Dispose();
				disposable = m_navigatorRotator as IDisposable;
				if (disposable != null)
					disposable.Dispose();
			}

			/// <summary>The navigation block chosen by the player or else the controller block.</summary>
			public PseudoBlock NavigationBlock
			{
				get { return m_navigationBlock ?? parent.NavigationBlock; }
				set { m_navigationBlock = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// <para>The block that will be landed</para>
			/// </summary>
			public PseudoBlock LandingBlock
			{
				get
				{
					if (parent == null)
						return m_landingBlock;
					return m_landingBlock ?? parent.LandingBlock;
				}
				set { m_landingBlock = value; }
			}

			public EnemyFinder EnemyFinder
			{
				get
				{
					if (parent == null)
						return m_enemyFinder;
					return m_enemyFinder ?? parent.EnemyFinder;
				}
				set { m_enemyFinder = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// <para>The navigator that is moving the ship</para>
			/// </summary>
			public INavigatorMover NavigatorMover
			{
				get
				{
					if (parent == null)
						return m_navigatorMover;
					return m_navigatorMover ?? parent.NavigatorMover;
				}
				set
				{
					m_navigatorMover = value;
					Logger.DebugLog("Nav Move: " + value);
				}
			}

			/// <summary>
			/// <para>May be null</para>
			/// <para>The navigator that is rotating the ship.</para>
			/// </summary>
			public INavigatorRotator NavigatorRotator
			{
				get
				{
					if (parent == null)
						return m_navigatorRotator;
					return m_navigatorRotator ?? parent.NavigatorRotator;
				}
				set
				{
					m_navigatorRotator = value;
					Logger.DebugLog("Nav Rotate: " + value);
				}
			}

			/// <summary>
			/// <para>Active complaints.</para>
			/// </summary>
			public InfoString.StringId Complaint
			{
				get
				{
					if (parent == null || m_complaint != InfoString.StringId.None)
						return m_complaint;
					return parent.Complaint;
				}
				set { m_complaint = value; }
			}

			/// <summary>ShipController will not run the navigator until after this time.</summary>
			public TimeSpan WaitUntil
			{
				get { return m_waitUntil ?? parent.WaitUntil; }
				set { m_waitUntil = value; }
			}

			/// <summary>Added to position of target block</summary>
			public PositionBlock DestinationOffset
			{
				get { return m_destinationOffset ?? parent.DestinationOffset; }
				set { m_destinationOffset = value; }
			}

			/// <summary>
			/// <para>May be null</para>
			/// <para>Information about which block to target</para>
			/// </summary>
			public BlockNameOrientation DestinationBlock
			{
				get
				{
					if (parent == null)
						return m_destBlock;
					return m_destBlock ?? parent.DestinationBlock;
				}
				set { m_destBlock = value; }
			}

			/// <summary>
			/// <para>The entity that is the target of the navigator, will be ignored by pathfinder.</para>
			/// <para>Block is used instead of grid.</para>
			/// </summary>
			public IMyEntity DestinationEntity
			{
				get
				{
					if (parent == null)
						return m_destEntity;
					return m_destEntity ?? parent.DestinationEntity;
				}
				set { m_destEntity = value; }
			}

			/// <summary>
			/// Determines if pathfinder should ignore an entity.
			/// </summary>
			public Func<IMyEntity, bool> IgnoreEntity
			{
				private get
				{
					if (parent == null)
						return m_ignoreEntity;
					return m_ignoreEntity ?? parent.IgnoreEntity;
				}
				set { m_ignoreEntity = value; }
			}

			/// <summary>
			/// For pathfinder to check if it should ignore an entity.
			/// </summary>
			public bool ShouldIgnoreEntity(IMyEntity entity)
			{
				return entity == DestinationEntity || IgnoreEntity.InvokeIfExists(entity);
			}

			/// <summary>How close the navigation block needs to be to the destination</summary>
			public float DestinationRadius
			{
				get { return m_destRadius ?? parent.DestinationRadius; }
				set { m_destRadius = value; }
			}

			public float DestinationRadiusSquared
			{
				get
				{
					float destRadius = DestinationRadius;
					return destRadius * destRadius;
				}
			}

			/// <summary>
			/// <para>Distance between current position and destination.</para>
			/// </summary>
			public float Distance
			{
				get { return m_distance ?? parent.Distance; }
				set { m_distance = value; }
			}

			/// <summary>
			/// <para>Will be NaN if Mover.Rotate() has not been called</para>
			/// <para>Angular distance between current direction and desired direction.</para>
			/// </summary>
			public float DistanceAngle
			{
				get { return m_distanceAngle ?? parent.DistanceAngle; }
				set { m_distanceAngle = value; }
			}

			/// <summary>
			/// <para>The desired speed of the ship</para>
			/// <para>Get returns the lowest value at this or higher level</para>
			/// <para>The lowest level of SpeedTarget belongs to pathfinder, it may be necessary to read at a higher level.</para>
			/// </summary>
			public float SpeedTarget
			{
				get
				{
					if (parent == null)
						return m_speedTarget.Value;
					if (m_speedTarget.HasValue)
						return Math.Min(m_speedTarget.Value, parent.SpeedTarget);
					return parent.SpeedTarget;
				}
				set { m_speedTarget = value; }
			}

			/// <summary>
			/// <para>The maximum speed relative to the target.</para>
			/// <para>Get returns the lowest value at this or higher level</para>
			/// <para>The lowest level of SpeedMaxRelative belongs to pathfinder, it may be necessary to read at a higher level.</para>
			/// </summary>
			public float SpeedMaxRelative
			{
				get
				{
					if (parent == null)
						return m_speedMaxRelative.Value;
					if (m_speedMaxRelative.HasValue)
						return Math.Min(m_speedMaxRelative.Value, parent.SpeedMaxRelative);
					return parent.SpeedMaxRelative;
				}
				set { m_speedMaxRelative = value; }
			}

			public float MinDistToJump
			{
				get { return m_minDistToJump ?? parent.MinDistToJump; }
				set { m_minDistToJump = value; }
			}

			/// <summary>Pathfinder should not run voxel tests.</summary>
			public bool IgnoreAsteroid
			{
				get { return m_ignoreAsteroid ?? parent.IgnoreAsteroid; }
				set { m_ignoreAsteroid = value; }
			}

			/// <summary>For final landing stage and "Line" command</summary>
			public bool PathfinderCanChangeCourse
			{
				get { return m_pathfindeCanChangeCourse ?? parent.PathfinderCanChangeCourse; }
				set { m_pathfindeCanChangeCourse = value; }
			}

			/// <summary>See "Form" command</summary>
			public bool Stay_In_Formation
			{
				get { return m_formation ?? parent.Stay_In_Formation; }
				set { m_formation = value; }
			}

		}

		public static float DefaultRadius { get { return 100f; } }

		public static float DefaultSpeed { get { return ServerSettings.GetSetting<float>(ServerSettings.SettingName.fDefaultSpeed); } }

		private readonly IMyCubeBlock defaultNavBlock;

		/// <summary>Settings that are reset at the start of commands. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Commands { get; private set; }

		/// <summary>Settings that are reset when a navigator rotator is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavRot { get; private set; }

		/// <summary>Settings that are reset when a navigator mover is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavMove { get; private set; }

		/// <summary>Settings that are reset when an engager is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavEngage { get; private set; }

		/// <summary>Settings that are reset when a waypoint navigator is finished. Settings should be written here but not read.</summary>
		public SettingsLevel Settings_Task_NavWay { get; private set; }

		/// <summary>Reflects the current state of autopilot. Settings should be read here but not written.</summary>
		public SettingsLevel Settings_Current { get { return Settings_Task_NavWay; } }

		public SettingsLevel GetSettingsLevel(SettingsLevelName level)
		{
			switch (level)
			{
				case SettingsLevelName.Commands:
					return Settings_Commands;
				case SettingsLevelName.NavRot:
					return Settings_Task_NavRot;
				case SettingsLevelName.NavMove:
					return Settings_Task_NavMove;
				case SettingsLevelName.NavEngage:
					return Settings_Task_NavEngage;
				case SettingsLevelName.NavWay:
					return Settings_Task_NavWay;
				default:
					throw new Exception("Unhandled enum: " + level);
			}
		}

		public PseudoBlock LastLandingBlock { get; set; }

		public Shopper Shopper { get; set; }

		public int WelderUnfinishedBlocks { get; set; }

		public event Action AfterTaskComplete;

		public AllNavigationSettings(IMyCubeBlock defaultNavBlock)
		{
			this.defaultNavBlock = defaultNavBlock;
			OnStartOfCommands();
		}

		public void OnStartOfCommands()
		{
			if (Settings_Commands != null)
				Settings_Commands.Dispose();
			Settings_Commands = new SettingsLevel(defaultNavBlock);
			OnTaskComplete_NavRot();
		}

		public void OnTaskComplete_NavRot()
		{
			if (Settings_Task_NavRot != null)
				Settings_Task_NavRot.Dispose();
			Settings_Task_NavRot = new SettingsLevel(Settings_Commands);
			OnTaskComplete_NavMove();
		}

		public void OnTaskComplete_NavMove()
		{
			if (Settings_Task_NavMove != null)
				Settings_Task_NavMove.Dispose();
			Settings_Task_NavMove = new SettingsLevel(Settings_Task_NavRot);
			OnTaskComplete_NavEngage();
		}

		public void OnTaskComplete_NavEngage()
		{
			if (Settings_Task_NavEngage != null)
				Settings_Task_NavEngage.Dispose();
			Settings_Task_NavEngage = new SettingsLevel(Settings_Task_NavMove);
			OnTaskComplete_NavWay();
		}

		public void OnTaskComplete_NavWay()
		{
			if (Settings_Task_NavWay != null)
				Settings_Task_NavWay.Dispose();
			Settings_Task_NavWay = new SettingsLevel(Settings_Task_NavEngage);
			AfterTaskComplete.InvokeIfExists();
		}

		public void OnTaskComplete(SettingsLevelName level)
		{
			switch (level)
			{
				case SettingsLevelName.Commands:
					OnStartOfCommands();
					return;
				case SettingsLevelName.NavRot:
					OnTaskComplete_NavRot();
					return;
				case SettingsLevelName.NavMove:
					OnTaskComplete_NavMove();
					return;
				case SettingsLevelName.NavEngage:
					OnTaskComplete_NavEngage();
					return;
				case SettingsLevelName.NavWay:
					OnTaskComplete_NavWay();
					return;
				default:
					throw new Exception("Unhandled enum: " + level);
			}
		}

		/// <summary>
		/// Gets a pretty formatted distance.
		/// </summary>
		/// <see cref="Rynchodon.PrettySI"/>
		/// <returns>A pretty formatted distance.</returns>
		public string PrettyDistance()
		{
			float distance = Settings_Current.Distance;
			if (float.IsNaN(distance))
				return string.Empty;
			if (distance < 1f)
				return "0 m";
			return PrettySI.makePretty(distance) + 'm';
		}

		/// <summary>
		/// Checks that the current angle to destination is less than value.
		/// Always returns false if the grid has not rotated towards the current destination.
		/// </summary>
		/// <param name="value">Value to compare to the angle</param>
		/// <returns>True iff angle is less than value and grid has rotated towards the destination.</returns>
		public bool DirectionMatched(float value = 0.1f)
		{
			float angle = Settings_Current.DistanceAngle;
			if (float.IsNaN(angle))
				return false;
			return angle < 0.1f;
		}

		/// <summary>
		/// Checks that the distance to the current destination is less than a given value.
		/// Allways returns false if the grid has not moved towards the current destination.
		/// </summary>
		/// <param name="value">Value to compare to the distance</param>
		/// <returns>True iff distance is less than value and grid has moved towards destination.</returns>
		public bool DistanceLessThan(float value)
		{
			float distance = Settings_Current.Distance;
			if (float.IsNaN(distance))
				return false;
			return distance < value;
		}

		/// <summary>
		/// Checks that the distance to the current destination is less than destination radius.
		/// Allways returns false if the grid has not moved towards the current destination.
		/// </summary>
		/// <returns>True iff distance is less than destination radius and grid has moved towards destination.</returns>
		public bool DistanceLessThanDestRadius()
		{
			return DistanceLessThan(Settings_Current.DestinationRadius);
		}

	}
}

using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;

namespace Rynchodon
{
	/// <summary>
	/// Makes sure a MyGameLogicComponent gets the right updates. Can only use UpdateAfterSimulation / 10 / 100.
	/// </summary>
	public abstract class UpdateEnforcer : MyGameLogicComponent
	{
		private MyEntityUpdateEnum value_EnforcedUpdate = MyEntityUpdateEnum.NONE;
		protected MyEntityUpdateEnum EnforcedUpdate
		{
			get { return value_EnforcedUpdate; }
			set { value_EnforcedUpdate = value; Entity.NeedsUpdate = value; }
		}

		private byte updateCount = 0;

		public override void UpdateAfterSimulation()
		{
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					alwaysLog("Method should be overriden", "UpdateAfterSimulation10()", Logger.severity.ERROR);
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					updateCount++;
					if (updateCount >= 10)
					{
						UpdateAfterSimulation10();
						updateCount = 0;
					}
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					updateCount++;
					if (updateCount >= 100)
					{
						UpdateAfterSimulation100();
						updateCount = 0;
					}
					return;
			}
		}

		public override void UpdateAfterSimulation10()
		{
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation10()", Logger.severity.WARNING);
					Entity.NeedsUpdate |= EnforcedUpdate;
					UpdateAfterSimulation();
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					alwaysLog("Method should be overriden", "UpdateAfterSimulation10()", Logger.severity.ERROR);
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					updateCount += 10;
					if (updateCount >= 100)
					{
						UpdateAfterSimulation100();
						updateCount = 0;
					}
					return;
			}
		}

		public override void UpdateAfterSimulation100()
		{
			switch (EnforcedUpdate)
			{
				case MyEntityUpdateEnum.EACH_FRAME:
					alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
					Entity.NeedsUpdate |= EnforcedUpdate;
					UpdateAfterSimulation();
					return;
				case MyEntityUpdateEnum.EACH_10TH_FRAME:
					alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
					Entity.NeedsUpdate |= EnforcedUpdate;
					UpdateAfterSimulation10();
					return;
				case MyEntityUpdateEnum.EACH_100TH_FRAME:
					alwaysLog("Method should be overriden", "UpdateAfterSimulation100()", Logger.severity.ERROR);
					return;
			}
		}

		public override void UpdatingStopped()
		{
			if (EnforcedUpdate != MyEntityUpdateEnum.NONE)
			{
				alwaysLog("Entity.NeedsUpdate set to " + Entity.NeedsUpdate + ", should be " + EnforcedUpdate, "UpdateAfterSimulation100()", Logger.severity.WARNING);
				Entity.NeedsUpdate |= EnforcedUpdate;
				UpdateAfterSimulation();
				return;
			}
		}

		protected MyObjectBuilder_EntityBase MyObjectBuilder;

		// sealed for now, to smooth transition
		public override sealed MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
		{
			if (MyObjectBuilder == null)
				return null;
			if (copy)
				return MyObjectBuilder.Clone() as MyObjectBuilder_EntityBase;
			return MyObjectBuilder;
		}

		/// <summary>
		/// does nothing
		/// </summary>
		public override sealed void UpdateBeforeSimulation() { }
		/// <summary>
		/// does nothing
		/// </summary>
		public override sealed void UpdateBeforeSimulation10() { }
		/// <summary>
		/// does nothing
		/// </summary>
		public override sealed void UpdateBeforeSimulation100() { }

		// needs a logging method for warnings and errors
		protected abstract void alwaysLog(string toLog, string method = null, Logger.severity level = Logger.severity.DEBUG);
	}
}

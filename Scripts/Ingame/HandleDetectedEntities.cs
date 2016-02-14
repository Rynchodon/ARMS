using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRageMath;

namespace Programmable
{
  public class HandleDetectedEntities : MyGridProgram
  {

    const string tab = "    ";

    /// <summary>These are defined by Rynchodon.AntennaRelay.ProgrammableBlock</summary>
    const char fieldSeparator = ',', entitySeparator = ';';

    const byte Relation_None = 0, Relation_Enemy = 1, Relation_Neutral = 2, Relation_Faction = 3, Relation_Owner = 4;
    const byte EntityType_None = 0, EntityType_Grid = 1, EntityType_Character = 2, EntityType_Missile = 3, EntityType_Unknown = 4;

    /// <summary>
    /// Time between sounding alarm (hours, minutes, seconds)
    /// </summary>
    TimeSpan alarmInterval = new TimeSpan(0, 0, 10);

    /// <summary>
    /// The next time the alarm will be allowed to sound.
    /// </summary>
    DateTime nextAlarmTime;

    List<TerminalActionParameter> displayEntities = new List<TerminalActionParameter>();

    public void Main(string arguments)
    {
      displayEntities.Clear();

      DetectedEntityData entityData;
      foreach (string serialized in arguments.Split(entitySeparator))
        if (DetectedEntityData.TryDeserialize(serialized, out entityData))
        {
          if (entityData.relations == Relation_Enemy)
          {
            displayEntities.Add(TerminalActionParameter.Get(entityData.entityId));

            // sound alarm if enemy is near
            if (DateTime.UtcNow >= nextAlarmTime &&
              (entityData.volume > 100 || entityData.volume == 0f) &&
              entityData.secondsSinceDetected < 60 &&
              Vector3D.DistanceSquared(Me.GetPosition(), entityData.predictedPosition) < 10000 * 10000)
            {
              nextAlarmTime = DateTime.UtcNow + alarmInterval;

              IMySoundBlock alarm = GridTerminalSystem.GetBlockWithName("Proximity Alarm") as IMySoundBlock;
              if (alarm == null)
                Echo("Proximity Alarm is not a sound block");
              else
                alarm.ApplyAction("PlaySound");
            }
          }
        }
        else
          Echo("Deserialize failed: " + entityData.name);

      IMyTextPanel panel = GridTerminalSystem.GetBlockWithName("Output Text Panel") as IMyTextPanel;
      if (panel == null)
        Echo("Output Text Panel is not a text panel block");
      else
      {
        ITerminalAction act = panel.GetActionWithName("DisplayEntities");
        if (act == null)
          Echo("ARMS actions are not loaded. ARMS must be present in the first world loaded after launching Space Engineers for actions to be loaded.");
        else
          panel.ApplyAction("DisplayEntities", displayEntities);
      }
    }

    /// <summary>
    /// Contains information about a detected entity.
    /// </summary>
    class DetectedEntityData
    {

      /// <summary>
      /// Attempts to create a DetectedEntityData from a string.
      /// </summary>
      /// <param name="serialized">The serialized object.</param>
      /// <param name="deserialized">The deserialized object</param>
      /// <returns>True iff the string could be deserialized.</returns>
      public static bool TryDeserialize(string serialized, out DetectedEntityData deserialized)
      {
        deserialized = new DetectedEntityData();
        string[] fields = serialized.Split(fieldSeparator);
        int index = 0;

        if (!long.TryParse(fields[index++], out deserialized.entityId))
          return false;
        if (!byte.TryParse(fields[index++], out deserialized.relations))
          return false;
        if (!byte.TryParse(fields[index++], out deserialized.type))
          return false;
        deserialized.name = fields[index++];
        if (!bool.TryParse(fields[index++], out deserialized.hasRadar))
          return false;
        if (!bool.TryParse(fields[index++], out deserialized.hasJammer))
          return false;
        if (!int.TryParse(fields[index++], out  deserialized.secondsSinceDetected))
          return false;
        {
          double x, y, z;
          if (!double.TryParse(fields[index++], out x))
            return false;
          if (!double.TryParse(fields[index++], out y))
            return false;
          if (!double.TryParse(fields[index++], out z))
            return false;
          deserialized.predictedPosition = new Vector3D(x, y, z);
        }
        {
          float x, y, z;
          if (!float.TryParse(fields[index++], out x))
            return false;
          if (!float.TryParse(fields[index++], out y))
            return false;
          if (!float.TryParse(fields[index++], out z))
            return false;
          deserialized.lastKnownVelocity = new Vector3(x, y, z);
        }

        // not always present
        float.TryParse(fields[index++], out deserialized.volume);

        return true;
      }

      public long entityId;
      public byte relations;
      public byte type;
      public string name;
      public bool hasRadar;
      public bool hasJammer;
      public int secondsSinceDetected;
      public Vector3D predictedPosition;
      public Vector3 lastKnownVelocity;
      public float volume;

      private DetectedEntityData() { }

    }

  }
}

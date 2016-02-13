using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace Programmable
{
  public class HandleDetectedEntities : MyGridProgram
  {

    const string tab = "    ";

    /// <summary>These are defined by Rynchodon.AntennaRelay.ProgrammableBlock</summary>
    const char fieldSeparator = ',', entitySeparator = ';';

    /// <summary>
    /// Time between sounding alarm (hours, minutes, seconds)
    /// </summary>
    TimeSpan alarmInterval = new TimeSpan(0, 0, 10);

    /// <summary>
    /// The next time the alarm will be allowed to sound.
    /// </summary>
    DateTime nextAlarmTime;

    List<DetectedEntityData> enemies = new List<DetectedEntityData>();

    public void Main(string arguments)
    {
      enemies.Clear();

      DetectedEntityData entityData;
      foreach (string serialized in arguments.Split(entitySeparator))
        if (DetectedEntityData.TryDeserialize(serialized, out entityData))
        {
          if (entityData.relations == "Enemy")
          {
            enemies.Add(entityData);

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

      StringBuilder text = new StringBuilder();
      text.AppendLine("Enemies:");
      for (int index = 0; index < enemies.Count && index < 20; index++)
      {
        text.Append(enemies[index].name);
        text.Append(tab);
        if (enemies[index].hasRadar)
        {
          text.Append("Has Radar");
          text.Append(tab);
        }
        if (enemies[index].hasJammer)
        {
          text.Append("Has Jammer");
          text.Append(tab);
        }
        text.Append(enemies[index].predictedPosition);
        text.Append(tab);
        text.AppendLine();
      }

      IMyTextPanel panel = GridTerminalSystem.GetBlockWithName("Output Text Panel") as IMyTextPanel;
      if (panel == null)
        Echo("Output Text Panel is not a text panel block");
      else
        panel.WritePublicText(text.ToString());
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
        deserialized.relations = fields[index++];
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
      public string relations;
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

using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Rynchodon.Weapons
{
	public class CharacterStateTracker
	{

		public static MyCharacterMovementEnum CurrentState(IMyEntity character)
		{
			CharacterStateTracker tracker;
			if (!Registrar.TryGetValue(character, out tracker))
			{
				Logger.AlwaysLog("Failed lookup of character: " + character.nameWithId(), Logger.severity.WARNING);
				return MyCharacterMovementEnum.Died; // most likely deleted
			}
			return tracker.m_currentState;
		}

		private MyCharacterMovementEnum m_currentState;

		public CharacterStateTracker(IMyCharacter character)
		{
			Registrar.Add((IMyEntity)character, this);
			character.OnMovementStateChanged += character_OnMovementStateChanged;
		}

		private void character_OnMovementStateChanged(MyCharacterMovementEnum oldState, MyCharacterMovementEnum newState)
		{
			m_currentState = newState;
		}

	}
}

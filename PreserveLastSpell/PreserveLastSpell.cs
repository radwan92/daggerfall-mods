using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace Game.Mods.PreserveLastSpell
{
    public class PreserveLastSpell : IHasModSaveData
    {
        [FullSerializer.fsObject("v1")]
        public class SaveData
        {
            public EffectBundleSettings? LastSpell;
        }

        public Type SaveDataType => typeof(SaveData);

        private static PreserveLastSpell instance;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            instance = new PreserveLastSpell();
            initParams.Mod.SaveDataInterface = instance;
        }

        public object NewSaveData()
        {
            return new SaveData();
        }

        public object GetSaveData()
        {
            EffectBundleSettings? lastSpell = GameManager.Instance.PlayerEffectManager.LastSpell?.Settings;

            Log($"Saving last spell: {lastSpell?.Name}:{lastSpell?.StandardSpellIndex}");

            return new SaveData
            {
                LastSpell = lastSpell,
            };
        }

        public void RestoreSaveData(object saveData)
        {
            EffectBundleSettings? lastSpell = ((SaveData)saveData).LastSpell;

            Log($"Restoring last spell: {lastSpell?.Name}:{lastSpell?.StandardSpellIndex}");

            if (lastSpell == null)
            {
                return;
            }

            try
            {
                var lastSpellBundle = new EntityEffectBundle(lastSpell.Value, GameManager.Instance.PlayerEntityBehaviour);
                typeof(EntityEffectManager).GetField("lastSpell", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(GameManager.Instance.PlayerEffectManager, lastSpellBundle);
            }
            catch (Exception exception)
            {
                Log($"Failed to restore last spell ({lastSpell.Value.Name}:{lastSpell.Value.StandardSpellIndex}): {exception.Message}");
            }
        }

        void Log(string message)
        {
            Debug.Log($"[{nameof(PreserveLastSpell)}] {message}");
        }
    }
}
using UnityEngine;
namespace DualPlay.Settings
{
    public class DualPlaySettings : ScriptableObject
    {
        [HideInInspector]
        public int maxNumberOfClients { get; set; }
        [Tooltip("Disabling this will increase the project size but will transfer project data like startup scene")]
        public bool linkLibrary;
        [HideInInspector]
        public string clonesPath { get; set; }

        private void OnEnable()
        {
            maxNumberOfClients = 1;
            linkLibrary = true;

            if (string.IsNullOrEmpty(clonesPath))
                clonesPath = Application.persistentDataPath;
        }
    }
}
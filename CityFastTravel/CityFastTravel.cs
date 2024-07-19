using System;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using UnityEngine;
using Mod = DaggerfallWorkshop.Game.Utility.ModSupport.Mod;

namespace Game.Mods.CityFastTravel
{
    public class CityFastTravel : MonoBehaviour
    {
        const float WorldScale = MapsFile.WorldMapTerrainDim * MeshReader.GlobalScale;

        bool debugMode = true;
        bool alwaysRevealBuildings = true;
        bool mapMouseClickCallbackRegistered;

        ExteriorAutomap exteriorAutomap;
        ExteriorAutomap ExteriorAutomap {
            get
            {
                if (exteriorAutomap == null)
                {
                    exteriorAutomap = FindObjectOfType<ExteriorAutomap>();
                }

                return exteriorAutomap;
            }
        }

        Camera ExteriorAutomapCamera => ExteriorAutomap != null
            ? ExteriorAutomap.CameraExteriorAutomap
            : null;

        int BlockSizeWidth => exteriorAutomap.BlockSizeWidth;
        int BlockSizeHeight => exteriorAutomap.BlockSizeHeight;
        int NumMaxBlocksX => exteriorAutomap.NumMaxBlocksX;
        int NumMaxBlocksY => exteriorAutomap.NumMaxBlocksY;
        float LayoutMultiplier => exteriorAutomap.LayoutMultiplier;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            var go = new GameObject("CityFastTravel");
            var cityFastTravel = go.AddComponent<CityFastTravel>();

            Mod mod = initParams.Mod;
            ModSettings modSettings = mod.GetSettings();

            cityFastTravel.debugMode = modSettings.GetBool("Debugging", "DebugMode");
            cityFastTravel.alwaysRevealBuildings = modSettings.GetBool("Misc", "AlwaysRevealBuildings");
        }

        void Awake()
        {
            DaggerfallUI.Instance.UserInterfaceManager.OnWindowChange += RegisterMapMouseClick;
        }

        void Start()
        {
            SetupBuildingsReveal();
        }

        void OnDestroy()
        {
            DeregisterMapMouseClick();
        }

        void FastTravel(BaseScreenComponent sender, Vector2 clickPosition)
        {
            SetupBuildingsReveal();

            try
            {
                PlayerGPS gps = GameManager.Instance.PlayerGPS;

                Log($"FastTravel initiated. " +
                    $"ClickPos: {clickPosition}, " +
                    $"PlayerPos: {gps.transform.position}," +
                    $"MapPixel: {gps.CurrentMapPixel.X}/{gps.CurrentMapPixel.Y}, " +
                    $"PlayerMarkerPos: {ExteriorAutomap.GameobjectPlayerMarkerArrow.transform.position}");

                Vector3 absoluteClickPosition = CalculateAutomapAbsoluteClickPosition(clickPosition);
                Vector3 worldPosition = MapPositionToWorldPosition(absoluteClickPosition);

                Log($"AbsoluteClickPos: {absoluteClickPosition}, WorldPos: {worldPosition}");

                if (!CheckIfLocationIsValid(absoluteClickPosition))
                {
                    return;
                }

                DaggerfallUI.Instance.FadeBehaviour.SmashHUDToBlack();
                DaggerfallUI.Instance.UserInterfaceManager.PopWindow();

                GameManager.Instance.StreamingWorld.TeleportToCoordinates(gps.CurrentMapPixel.X, gps.CurrentMapPixel.Y, worldPosition);
                // Required since `TeleportToCoordinates` doesn't set the RepositionMethod
                GameManager.Instance.StreamingWorld.SetAutoReposition(StreamingWorld.RepositionMethods.Offset, worldPosition);

                Log($"FastTravel finished. " +
                    $"PlayerPos: {gps.transform.position}, " +
                    $"MapPixel: {gps.CurrentMapPixel.X}/{gps.CurrentMapPixel.Y}");

                DaggerfallUI.Instance.FadeBehaviour.FadeHUDFromBlack();
            }
            catch (Exception exception)
            {
                Log($"FastTravel failed: {exception.Message}");
            }
        }

        /// <summary>
        /// Checks if the location at the given map position is valid, i.e. it is
        /// a free space and not a building.
        /// </summary>
        bool CheckIfLocationIsValid(Vector3 mapAbsolutePosition)
        {
            // Absolute position start from the center of the location, we need to shift it to the corner
            mapAbsolutePosition.x += BlockSizeWidth * NumMaxBlocksX * 0.5f;
            mapAbsolutePosition.z += BlockSizeHeight * NumMaxBlocksY * 0.5f;

            // Get automap texture
            Transform canvas = null;
            Transform mapTransform = ExteriorAutomap.transform;
            for (int i = 0; i < mapTransform.childCount; i++)
            {
                var child = mapTransform.GetChild(i);
                if (child.name.Contains("Canvas"))
                {
                    canvas = child;
                    break;
                }
            }

            if (canvas == null)
            {
                Log("Canvas not found.");
                return true;
            }

            var renderer = canvas.GetComponent<MeshRenderer>();
            var mapTexture = renderer.sharedMaterial.mainTexture;

            // Get pixel at click position
            int x = (int)Mathf.Clamp(mapAbsolutePosition.x, 0, mapTexture.width);
            int y = (int)Mathf.Clamp(mapAbsolutePosition.z, 0, mapTexture.height);

            Color mapPixel = ((Texture2D)mapTexture).GetPixel(x, y);

            // Transparent pixel = free real estate
            bool validLocation = mapPixel.a == 0;
            if (!validLocation)
            {
                Log($"Invalid location at {mapAbsolutePosition}. Pixel: {mapPixel}");
            }

            return validLocation;
        }

        /// <summary>
        /// Given a relative click position (GUI based) on the automap,
        /// calculate the absolute click position (transform based).
        /// </summary>
        Vector3 CalculateAutomapAbsoluteClickPosition(Vector2 clickPosition)
        {
            float orthographicSize = ExteriorAutomapCamera.orthographicSize;
            float aspect = ExteriorAutomapCamera.aspect;

            var camClickDelta = new Vector3
            {
                x = (clickPosition.x / ExteriorAutomapCamera.pixelWidth) * orthographicSize * aspect * 2 - orthographicSize * aspect,
                y = (clickPosition.y / ExteriorAutomapCamera.pixelHeight) * orthographicSize * 2 - orthographicSize
            };

            // Rotate the click delta to match the camera rotation on XY plane
            camClickDelta = Quaternion.Euler(0, 0, ExteriorAutomapCamera.transform.rotation.eulerAngles.y) * camClickDelta;

            Vector3 absoluteClickPosition = ExteriorAutomapCamera.transform.position;
            absoluteClickPosition.x += camClickDelta.x;
            absoluteClickPosition.z -= camClickDelta.y;

            return absoluteClickPosition;
        }

        /// <summary>
        /// Converts an absolute map position to an in-world position. Y coordinate is set to a <see cref="float.MinValue"/>.
        /// Based on <see cref="DaggerfallWorkshop.Game.ExteriorAutomap.UpdatePlayerMarker"/>.
        /// </summary>
        Vector3 MapPositionToWorldPosition(Vector3 mapPosition)
        {
            float xOffset = 0.0f;
            float yOffset = 0.0f;

            bool isCustomLocation = GameManager.Instance.PlayerGPS.CurrentLocation.HasCustomLocationPosition();
            if (isCustomLocation)
            {
                xOffset = -64f;
                yOffset =  +3f;
            }

            int refWidth = (int)(BlockSizeWidth * NumMaxBlocksX * LayoutMultiplier);
            int refHeight = (int)(BlockSizeHeight * NumMaxBlocksY * LayoutMultiplier);

            Vector3 worldPosition = mapPosition;
            worldPosition.y = float.MinValue; // Set to minimum to ensure player is placed on ground

            worldPosition.x -= -(refWidth * 0.5f) + xOffset;
            worldPosition.z -= -(refHeight * 0.5f) + yOffset;

            worldPosition.x /= refWidth;
            worldPosition.z /= refHeight;

            return worldPosition * WorldScale;
        }

        void RegisterMapMouseClick(object sender, EventArgs eventArgs)
        {
            if (mapMouseClickCallbackRegistered)
            {
                return;
            }

            DaggerfallExteriorAutomapWindow window = DaggerfallUI.Instance.ExteriorAutomapWindow;
            if (window?.PanelRenderAutomap == null)
            {
                return;
            }

            SetupBuildingsReveal();

            Log("Registering map mouse click callback.");

            window.PanelRenderAutomap.OnMiddleMouseClick += FastTravel;

            mapMouseClickCallbackRegistered = true;
        }

        void DeregisterMapMouseClick()
        {
            if (!mapMouseClickCallbackRegistered)
            {
                return;
            }

            DaggerfallExteriorAutomapWindow window = DaggerfallUI.Instance.ExteriorAutomapWindow;
            if (window?.PanelRenderAutomap == null)
            {
                return;
            }

            Log("Deregistering map mouse click callback.");

            window.PanelRenderAutomap.OnMiddleMouseClick -= FastTravel;

            mapMouseClickCallbackRegistered = false;
        }

        void SetupBuildingsReveal()
        {
            if (!alwaysRevealBuildings)
            {
                return;
            }

            ExteriorAutomap automap = ExteriorAutomap;
            if(automap.RevealUndiscoveredBuildings)
            {
                return;
            }

            Log("Revealing all buildings enabled.");
            ExteriorAutomap.RevealUndiscoveredBuildings = true;
        }

        void Log(string message)
        {
            if (!debugMode)
            {
                return;
            }

            Debug.Log($"[{nameof(CityFastTravel)}] {message}");
        }

        void OnGUI()
        {
            if (!debugMode)
            {
                return;
            }

            DrawDebugPlayerPosition();
            DrawDebugCameraDetails();
        }

        void DrawDebugPlayerPosition()
        {
            PlayerGPS gps = GameManager.Instance.PlayerGPS;
            GUILayout.Label($"PlayerPos: {gps.transform.position}, PixelPos: {gps.CurrentMapPixel.X}/{gps.CurrentMapPixel.Y}");
        }

        void DrawDebugCameraDetails()
        {
            Camera automapCam = ExteriorAutomapCamera;
            if (automapCam == null)
            {
                return;
            }

            GUILayout.Label($"CamSize: {automapCam.pixelRect.size}, " +
                            $"CamPos: {automapCam.transform.position}, " +
                            $"OrthoSize: {automapCam.orthographicSize}/{automapCam.aspect*automapCam.orthographicSize}");
        }
    }
}

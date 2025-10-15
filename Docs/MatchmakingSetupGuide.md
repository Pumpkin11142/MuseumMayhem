# Matchmaking Setup Guide (Mirror 96.0.1 + Unity 6)

The steps below assume you already imported Mirror 96.0.1 into your Unity 6 project. Follow each step in order. Every click, drag, and value change is written out so you can hand this guide to anyone and they can finish the setup.

---

## 1. Prepare your scenes

1. **Open the Main Menu scene.** This scene is the screen that appears when players launch the game.
2. **Open the Gameplay scene** in a second tab so you can switch between the two scenes while you work. This is the scene where the actual round happens.

> You will assign the Main Menu scene as the "Room" scene and the Gameplay scene as the "Gameplay" scene later. Mirror will move players from the room to the gameplay scene automatically when a match starts.

---

## 2. Add the Custom Network Manager (Main Menu scene)

1. In the **Hierarchy** window, right-click and choose **Create Empty**.
2. Rename the new GameObject to **`NetworkManager`** so it is easy to find.
3. With `NetworkManager` selected, click **Add Component** in the Inspector.
4. Search for **`CustomNetworkManager`** and add it. (This script now lives in `Scripts/Handlers-Managers/CustomNM.cs`.)
5. Still on the same GameObject, click **Add Component** again.
6. Search for **`Kcp Transport`** (or the transport you prefer) and add it. Mirror needs exactly one transport component on the same GameObject as the network manager.
7. In the Inspector for `CustomNetworkManager`, fill in these fields:
   - **Offline Scene / Room Scene**: drag the Main Menu scene asset from the Project window onto this box.
   - **Gameplay Scene**: drag the Gameplay scene asset here.
   - **Player Prefab**: drag the player prefab you want to spawn in the gameplay scene.
   - **Room Player Prefab**: leave empty for now – you will create it in the next section.
   - **Max Connections**: set this value to the number of players that should be in one match (for example, type `2`). Make sure it matches the `Players Per Match` value (explained later in Section 4).
   - **Players Per Match**: type the same number you set in **Max Connections** (for example, `2`).
   - **Match Start Delay**: type how many seconds you want to show a "Match found" message before loading the gameplay scene (for example, `2`).
> **Important:** The `NetworkManager` GameObject must be present in the Main Menu scene. Do not duplicate it in the Gameplay scene. Mirror keeps the object alive when scenes change. The UI script automatically finds the manager even after Unity moves it to `DontDestroyOnLoad` during play mode.

---

## 3. Create the room-player prefab (lobby placeholder)

Mirror keeps a lightweight “room player” object for each connection while players wait in the menu. The `MatchmakingRoomPlayer` script handles ready states and UI updates.

1. In the **Project** window, right-click inside a folder where you store prefabs (for example, `Assets/Prefabs/Networking`).
2. Choose **Create → Prefab** (or drag an empty GameObject from the Hierarchy into the folder to create a prefab).
3. Name the prefab **`RoomPlayer`**.
4. Double-click the prefab to open it in Prefab Mode.
5. In the prefab Inspector, click **Add Component** and add these components in this exact order:
   1. **Network Identity** (required by Mirror).
   2. **Matchmaking Room Player** (script found at `Scripts/Handlers-Managers/MatchmakingRoomPlayer.cs`). This script automatically inherits `NetworkRoomPlayer`, so you do **not** add a separate `NetworkRoomPlayer` component.
6. Press **Ctrl+S** (or **Cmd+S** on macOS) to save the prefab and exit Prefab Mode.
7. Go back to the Main Menu scene, select the `NetworkManager` GameObject, and drag the new **RoomPlayer** prefab into the **Room Player Prefab** field on `CustomNetworkManager`.

---

## 4. Configure matchmaking settings

1. With the `NetworkManager` still selected, look at the **Matchmaking Settings** group in the Inspector.
2. **Players Per Match**: type how many players are required to start (for example, `2`).
3. **Match Start Delay**: type how many seconds to wait after both players ready up before loading the Gameplay scene.
4. Confirm that **Max Connections** (near the top of the Inspector) matches the same number. This keeps one match per server instance and prevents extra people from joining mid-round.

> If you ever raise **Players Per Match**, remember to update **Max Connections** too.

---

## 5. Hook up the Main Menu UI

1. Locate the ready-up button in your Main Menu canvas.
2. Select the UI GameObject that should control the matchmaking (for example, an empty object named `MatchmakingPanel`).
3. Click **Add Component** and add **`Main Menu Matchmaking UI`** (script at `Scripts/Handlers-Managers/MainMenuMatchmakingUI.cs`).
4. With the new component visible, drag references from the Hierarchy into each slot:
   - **Ready Button**: drag the Button component that players press to ready up.
   - **Ready Button Label**: drag the TextMeshProUGUI component that shows the button text (for example, the child text object inside the button). If you use the legacy `Text` component instead of TextMeshPro, replace the `TMP_Text` field in the Inspector with your Text component – Unity will handle the conversion.
   - **Status Label**: drag the TextMeshProUGUI (or Text) element that should display messages such as “Looking for another player…”.
   - **Searching Indicator**: drag any spinner or animated GameObject you want to show while waiting. If you do not have one, leave this field empty (the script checks for `null`).
5. Set **Server Address** to the IP or domain of the server that will host matches. For local testing on one machine, leave it at `localhost`.
6. Decide whether you want the Unity Editor to auto-host when you click **Play**:
   - Leave **Start Host In Editor** checked to make the editor instance become the host automatically when you press the Ready button.
   - Uncheck it if you prefer to run a separate dedicated server even while testing.

> When a player clicks Ready, the script first connects to the server. If it is running inside the Unity Editor and **Start Host In Editor** is enabled, it will start hosting automatically. Once connected, clicking Ready toggles the ready state. The button text switches between “Ready Up” and “Cancel”, and the status label explains what is happening. Mirror 96 replaces the older `hasAuthority` property with `isOwned`; the scripts already use the new property so no additional changes are needed on your end.

---

## 6. Prepare the Gameplay scene

1. Open the Gameplay scene.
2. Add (or confirm) a `DynamicSpawnSystem` GameObject:
   1. If you already have one, make sure it uses the script located at `Scripts/Handlers-Managers/DynamicPlayerSpawn.cs`.
   2. If you do not have one, create an empty GameObject, name it **`SpawnSystem`**, and click **Add Component → Dynamic Spawn System**.
   3. Assign the **Spawn Center**, **Spawn Radius**, and any other fields you already use.
3. Make sure the scene contains the player prefab referenced earlier under **Player Prefab** (the prefab itself will be spawned automatically; it does not need to be in the scene).
4. Save the scene.

> The `CustomNetworkManager` automatically looks for `DynamicSpawnSystem` each time the gameplay scene loads. If it cannot find one it will fall back to the default NetworkManager start positions.

---

## 7. Build a headless server (optional but recommended)

1. Open **File → Build Settings**.
2. Ensure both the Main Menu scene and the Gameplay scene are added to the **Scenes In Build** list (Main Menu first, Gameplay second).
3. Create a dedicated server build:
   1. Choose your target platform (Windows, macOS, or Linux).
   2. Tick **Server Build** if the platform supports it (Windows/Linux). For macOS, you can add the command-line arguments manually later.
   3. Click **Build** and choose a folder (for example, `Builds/Server`).
4. To run the headless server, start the build with these arguments:
   - Windows: `GameName.exe -batchmode -nographics`.
   - macOS/Linux: `./GameName.x86_64 -batchmode -nographics`.
5. When running the dedicated server, leave **Start Host In Editor** unchecked on client builds so they only connect as clients.
6. Keep the `ServerImguiBootstrap` script (located at `Scripts/Handlers-Managers/ServerImguiBootstrap.cs`) in the project. It prevents Unity from stripping the IMGUI module in server builds so Mirror’s internal `OnGUI` method does not trigger runtime errors.

---

## 8. Test the matchmaking flow locally

1. Press **Play** in the Unity Editor. The Main Menu should load.
2. Click the Ready button. Because you are in the editor, the script will host locally (if **Start Host In Editor** is enabled). The status text changes to “Looking for another player…”.
3. Build and run a standalone client (or start a second Editor using Unity’s "Enter Play Mode" in a different project copy). In the build, leave **Start Host In Editor** unchecked, enter `localhost` as the address, and click Ready.
4. Both clients should now display “Match found! Starting in X seconds.” After the delay, Mirror automatically loads the Gameplay scene on both clients and spawns the player prefab using the `DynamicSpawnSystem` positions.
5. When a player presses the Ready button again after returning to the Main Menu, the cycle repeats.

---

## 9. Tips for production use

- **Scaling**: Run one server instance per match for the simplest setup. Each instance handles exactly the number of players you set in **Players Per Match**.
- **Custom addresses**: Replace `localhost` with the IP or domain of your dedicated server in `MainMenuMatchmakingUI` so builds know where to connect.
- **Cancel ready**: Players can click the button again to cancel while waiting. The countdown stops, and the server sends the "Match cancelled" message so everyone’s UI updates.
- **Scene safety**: Never put a second `NetworkManager` in the Gameplay scene. Use only the one from the Main Menu.
- **Transport choice**: You can swap `Kcp Transport` for Mirror’s `SimpleWebTransport` or another option if you need WebGL or relay-style networking. Just keep the transport component on the same GameObject as `CustomNetworkManager`.

---

Following these steps gives you:

- A ready-up button that queues players.
- Automatic pairing once the required number of players are ready.
- Scene transitions handled by Mirror without manual loading code.
- Clear, player-facing UI feedback for every matchmaking state.

If you run into issues, double-check that the scene references are assigned on the `NetworkManager`, the room player prefab has a `Network Identity`, and the dedicated server build is running before clients attempt to connect.

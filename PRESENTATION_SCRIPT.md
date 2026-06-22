# RecycleVision Lite Presentation Script

## 15-Minute Structure

Use this timing as your guide:

- **0:00 - 1:00** Project introduction
- **1:00 - 3:00** User experience and mechanics
- **3:00 - 6:30** Technical implementation
- **6:30 - 8:30** ML-Agents / AI explanation
- **8:30 - 12:30** Live demo
- **12:30 - 14:00** Results, challenges, improvements
- **14:00 - 15:00** Conclusion and transition to questions

---

## Before Presenting

Do this before your turn starts:

1. Open Unity.
2. Open the scene:
   `Assets/Scenes/newest.unity`
3. In Unity, select:
   `Tools > RecycleVision > ML Agents > Use Inference (Model)`
4. Press Play once before presenting and confirm there are no red Console errors.
5. Start the backend if you want to show the API/data part:
   ```powershell
   .\backend\run_backend.ps1
   ```
6. Keep these URLs ready in the browser:
   ```text
   http://127.0.0.1:8000/health
   http://127.0.0.1:8000/recyclevision/stats
   http://127.0.0.1:8000/dashboard
   ```
7. Have a backup screen recording ready in case the live demo fails.

---

## 0:00 - 1:00 Project Introduction

Hello, my project is called **RecycleVision Lite**.

It is a 3D household recycling sorting assistant made in Unity. The idea is to help users learn how to sort everyday waste into the correct recycling category.

The scene represents a small recycling station with four bins:

- Plastic
- Paper and cardboard
- Glass
- Organic waste

Items spawn one at a time, and the user can inspect them, pick them up, and place them into a bin. The system gives immediate feedback and also shows an AI suggestion.

The main goal was not only to make a small sorting game, but to connect it with an AI workflow using **Unity ML-Agents**, visual observations, and a trained policy model.

---

## 1:00 - 3:00 Mechanics And User Experience

The project has two main modes.

The first mode is **Training Mode**. In this mode, the user sorts items manually. The game is slower and more focused on learning. When an item appears, the AI gives a suggested bin, and the player can compare their own choice with the AI suggestion. After sorting, the system gives feedback explaining whether the choice was correct.

The second mode is **Quick Sort Mode**. This mode is faster and more automatic. It is useful for showing the AI behaviour and the robot sorting flow. The system can apply the AI decision and move the item toward the predicted bin.

The sorting is based on trigger zones inside the bins. When an item enters a bin trigger, Unity checks the selected bin against the item's correct category. Then the HUD, dashboard, feedback text, and session statistics update.

The interaction is keyboard and mouse based:

- WASD moves the camera.
- Mouse is used to look around and interact.
- The user can grab and drop waste items.
- The scene includes visual highlights, labels, feedback, and a live dashboard.

---

## 3:00 - 6:30 Technical Implementation

Technically, the project is split into several systems.

The **SortingStationManager** controls the main session flow. It starts sessions, spawns items, tracks the current mode, receives AI decisions, applies feedback, and updates the HUD and dashboard.

The **WasteItemDefinition** class defines the item data. Each item has:

- A display name
- A correct bin
- A common mistake bin
- A visual shape
- Colors and scale
- A prefab model
- A training hint

The **WasteItemFactory** creates the item in the scene. It loads the visual prefab, removes unsafe imported physics components, creates a clean collider, and adds a Rigidbody so the item can be moved and sorted.

This was important because some downloaded models had mesh colliders and Rigidbodies that caused Unity physics problems. For example, concave MeshColliders cannot be used with dynamic Rigidbodies. I fixed this by making the spawned item use a clean collider setup instead of relying on imported model colliders.

The project also has a **HUD** and a **DashboardBoard**. These show:

- Current item
- AI suggested bin
- Feedback
- Session accuracy
- Mistake patterns
- Human and AI performance information

There is also optional backend integration using FastAPI. Unity sends session logs to a local API with information like:

- True bin
- Selected bin
- Predicted bin
- AI confidence
- Item features
- ML observation vector

This makes the project stronger because it connects Unity gameplay, AI, data logging, and visual analysis.

---

## 6:30 - 8:30 AI And ML-Agents Explanation

The AI part uses **Unity ML-Agents**.

The agent is called **RecycleVisionMlAgent**. It observes the current sorting scene and outputs one discrete action:

- 0 means Plastic
- 1 means Paper/Cardboard
- 2 means Glass
- 3 means Organic

The important part is that the agent has an **84 by 84 RGB CameraSensor**. This means ML-Agents receives an image observation, and ML-Agents processes that image using a visual encoder. In the training configuration, the visual encoder type is set to `simple`, which is a CNN-style encoder used for visual observations.

For honest wording, I describe it like this:

> The sorting AI is an ML-Agents policy that uses a CNN visual encoder from the CameraSensor, together with supporting item feature observations, to choose one of four recycling actions.

The agent also uses 13 vector observations. These include simplified information such as item visual shape, primary color, accent color, and scale. These extra features help stabilize learning and make the demo more reliable.

The reward system is:

- Positive reward for the correct bin
- Negative reward for the wrong bin
- Small step penalty to encourage fast decisions

The trained model is exported as:

```text
Assets/RecycleVision/Models/RecycleVisionSorter.onnx
```

At runtime, Unity uses this ONNX model in inference mode. When an item appears, the ML agent predicts the bin, the system highlights that bin, and the UI shows the AI suggestion.

One important design decision is that the project is not a pure supervised CNN classifier. It is an ML-Agents reinforcement learning policy with a CNN visual encoder and extra structured observations. This still matches the idea of using ML-Agents for sorting based on visual input, but it is more stable for a short Unity project demo.

---

## 8:30 - 12:30 Live Demo Steps

### Demo Step 1: Show The Scene

Say:

Here is the 3D sorting station. We can see the four recycling bins, the item spawn area, the dashboard, and the AI feedback area.

Show:

- The four bins
- The item spawn area
- The dashboard board
- The HUD

### Demo Step 2: Training Mode

Say:

I will first show Training Mode. This mode is focused on learning. An item appears, the AI suggests a bin, and the user can still make the final decision.

Do:

1. Start or reset a Training Mode session.
2. Let an item spawn.
3. Point out the AI suggestion.
4. Pick up the item.
5. Drop it in the correct bin.
6. Show the feedback.

Say:

The bin trigger detects the item. The system checks if the selected bin matches the item's correct recycling category. Then the feedback and dashboard update immediately.

### Demo Step 3: Show A Mistake If Useful

Only do this if the demo is going smoothly.

Say:

I can also intentionally place an item in the wrong bin to show that the system records mistakes and updates the dashboard.

Do:

1. Put one item in the wrong bin.
2. Show the feedback.
3. Show the mistake count or dashboard update.

### Demo Step 4: Quick Sort Mode

Say:

Now I will show Quick Sort Mode. This mode is faster and is better for showing the AI and robot-style automatic sorting flow.

Do:

1. Switch to Quick Sort Mode.
2. Let the AI suggest a bin.
3. Show the item being sorted automatically if available.
4. Point out that the dashboard continues updating.

Say:

In this mode, the trained ML-Agents policy can control the sorting decision. This lets us compare AI behaviour and user performance over a session.

### Demo Step 5: Show Backend / Stats

If the backend is running, open:

```text
http://127.0.0.1:8000/recyclevision/stats
```

Say:

The optional backend stores session logs. This means the Unity scene is not only a visual demo; it also produces data that can be analyzed later.

Show:

- The stats endpoint
- Or the dashboard endpoint if it looks better:
  ```text
  http://127.0.0.1:8000/dashboard
  ```

Say:

The logged data includes the true category, selected category, predicted category, AI confidence, and item features.

### Demo Step 6: Show ML Files Quickly

If there is time, show these files in Unity or the file explorer:

```text
Assets/RecycleVision/Models/RecycleVisionSorter.onnx
Assets/RecycleVision/ML/Configs/recyclevision_sorter_ppo.yaml
Assets/RecycleVision/ML/TRAINING_NOTES.txt
```

Say:

This is the trained ONNX model used at runtime, and this is the training configuration used for ML-Agents.

---

## 12:30 - 14:00 Results And Challenges

The main result is that the project has a complete Unity sorting workflow:

- Items spawn one by one.
- The user can interact with them.
- Bins detect sorting decisions.
- The AI gives suggestions.
- The dashboard updates live.
- Session data can be stored in a backend.
- A trained ML-Agents model is used in Unity inference mode.

One challenge was designing the AI setup. At first, a pure visual classifier sounds simple, but in Unity ML-Agents the usual approach is to train an agent through reinforcement learning. So I used a CameraSensor for visual input and a discrete action space for the four bins.

Another challenge was making training reliable. I added supporting vector observations and action masking to make the agent learn more consistently. This is a practical design decision because the project is a demo and needs stable behaviour during presentation.

A third challenge was physics. Some downloaded assets had concave MeshColliders with dynamic Rigidbodies, which caused Unity runtime warnings. I solved this by cleaning the physics setup and using safe colliders for spawned waste items.

If I had more time, I would improve the AI by training with more varied random item positions, more parallel environments, and possibly a separate supervised CNN classifier trained on screenshots of the items. That would make the visual classification part even stronger.

---

## 14:00 - 15:00 Conclusion

To conclude, RecycleVision Lite is a Unity recycling assistant that combines 3D interaction, AI sorting, data visualization, and optional backend logging.

The project fulfills the main goals:

- A 3D recycling sorting station
- Four waste categories
- Manual and AI-assisted sorting
- Training and quick-sort modes
- Live feedback and dashboard
- ML-Agents model with visual CameraSensor input
- Runtime inference using a trained ONNX model
- Optional FastAPI logging and stats

The main thing I learned is how to connect gameplay mechanics with ML-Agents. The AI is not just separate from the scene; it is integrated into the actual sorting flow, feedback system, and data dashboard.

That is my project. Thank you.

---

## Short Emergency Version

If you are running out of time, say this:

RecycleVision Lite is a Unity 3D recycling assistant. The user sorts household waste into Plastic, Paper/Cardboard, Glass, and Organic bins. Items spawn one by one, the user can grab and drop them, and bin trigger zones detect the result.

The project has Training Mode for slower learning with explanations, and Quick Sort Mode for faster AI-assisted sorting. A dashboard updates live with accuracy and mistakes.

The AI uses Unity ML-Agents. The agent has an 84x84 RGB CameraSensor, so ML-Agents processes visual input with a CNN-style encoder. It also uses supporting vector observations and outputs one of four discrete bin actions. The trained model is exported as `RecycleVisionSorter.onnx` and used in Unity inference mode.

The optional FastAPI backend stores session logs and exposes stats through endpoints like `/recyclevision/stats`.

The biggest challenges were ML-Agents training stability and Unity physics issues with imported mesh colliders. Both were solved by adding structured observations/action masking and cleaning the spawned item collider setup.

---

## Likely Questions And Answers

### Question: Why did you use ML-Agents instead of a normal CNN classifier?

Answer:

Because the project is inside Unity and the requirement suggested ML-Agents. ML-Agents lets the AI interact directly with the scene, observe through a CameraSensor, and output actions inside the Unity environment. A normal CNN classifier would classify screenshots, but ML-Agents connects the prediction directly to the game logic and sorting action.

### Question: Is your model really CNN-based?

Answer:

It uses a CameraSensor visual observation, and ML-Agents uses a visual encoder for image input. In my config the visual encoder is `simple`, which is a CNN-style encoder. I also use vector observations to improve reliability, so it is not a pure CNN-only classifier. It is an ML-Agents policy with CNN visual input plus supporting features.

### Question: What is the action space?

Answer:

The action space is discrete with one branch and four possible actions:

- 0 = Plastic
- 1 = Paper/Cardboard
- 2 = Glass
- 3 = Organic

### Question: What are the observations?

Answer:

The agent observes an 84x84 RGB camera image through a CameraSensor. It also receives 13 vector observations representing simplified item features such as shape, colors, and scale.

### Question: What reward function did you use?

Answer:

The agent gets a positive reward for choosing the correct bin, a negative reward for choosing the wrong bin, and a small step penalty. This encourages correct and fast decisions.

### Question: What is action masking?

Answer:

Action masking disables impossible or unreasonable actions for some obvious item families. For example, a paper box should not need all four actions available. This helps training become more stable and avoids wasting learning on impossible choices.

### Question: How do bins detect sorting?

Answer:

Each bin has a trigger zone. When a WasteItem enters the trigger, Unity checks the bin category against the item's correct category. Then the manager updates feedback, stats, dashboard, and session history.

### Question: What was the hardest technical problem?

Answer:

The hardest parts were making the ML-Agents setup stable and fixing imported asset physics. Some downloaded prefabs had concave MeshColliders with dynamic Rigidbodies, which Unity does not support. I fixed this by cleaning imported colliders and using safe generated colliders for spawned items.

### Question: What would you improve next?

Answer:

I would train with more randomized item positions and lighting, use more parallel environments, and possibly add a separate supervised CNN classifier trained on generated screenshots. I would also improve the dashboard with more per-class confusion matrices.

---

## Phrases To Use Safely

Use:

- "ML-Agents policy with a CNN visual encoder"
- "CameraSensor visual observations"
- "discrete action output for the four bins"
- "reinforcement learning inside Unity"
- "supporting vector observations for training stability"
- "trained ONNX model used in inference mode"

Avoid saying:

- "It is a pure CNN-only classifier"
- "The agent only sees the camera image"
- "I used multiple parallel environments" unless you actually show evidence


# BCIKeyboardXR - Implementation Notes

## Running the Scene

1. Open `Assets/Scenes/Main.unity` in Unity 6 LTS.
2. Confirm `Assets/Resources/config.json` exists locally with a valid Anthropic API key. See `README.md` for the expected format.
3. Press Play. The scene contains `PredictionService` and `AppController`; the controller builds the Canvas, rows, keyboard, buttons, and EventSystem at runtime.

## Architecture

The runtime is organized into three layers:

- **Core** (`Assets/Scripts/Core/`) - user profile loader, shared state.
- **LLM** (`Assets/Scripts/LLM/`) - Anthropic API client, prediction service, config loader, prediction DTOs.
- **UI** (`Assets/Scripts/UI/`) - the runtime UI built at startup. AppController orchestrates the event flow between predictions, row controllers, composition state, and animations.

The composition state, keyboard, and prediction rows are decoupled. Each communicates through events. AppController is the only component that knows about all of them.

## Known Limitations and Placeholders

- **Speak is a placeholder.** It logs `[Speak] <sentence>` and pulses the composition bar. Real text-to-speech integration is deferred. See "Future Work" in `README.md`.
- **Mouse stands in for gaze; hover-dwell stands in for SSVEP confirmation.** No physical eye tracker, XR ray, or SSVEP classifier is implemented. The interaction model is designed to be drop-in replaceable with real gaze + SSVEP input.
- **Tuned for 1920x1080 standalone desktop.** Other aspect ratios are not polished. A production deployment on an XR headset would re-layout into the user's frontal field of view.
- **Frosted glass is rendered with layered UI sprites**, not true backdrop blur. This avoids dependency on custom render features and holds 60 fps with all tiles flickering simultaneously. See `VISIONOS_AESTHETIC_RESEARCH.md` for the design rationale.

## Suggested Walkthrough

To exercise the full system on a first run:

1. Start from empty context. Phrase suggestions populate within ~1s, conditioned on the user profile (`Assets/Resources/user_profile.md`).
2. Type `i` `n` `e`. The word row populates with completions for the partial word.
3. Dwell over a word tile, such as `need`. The ghost text preview shows the resulting sentence in the composition bar.
4. Hold the dwell to commit. The selected word punches, flashes, and flies to the composition bar with a glow pulse.
5. Continue with `to` or `my`. Phrase predictions refresh to reflect the new context.
6. Dwell over a phrase tile. The full continuation appears as ghost text.
7. Commit the phrase. The partial word in the composition is stripped and the phrase is appended cleanly.
8. Press Backspace once. The entire committed phrase is removed in one action through smart backspace.
9. Press Speak to log the assembled sentence and pulse the composition bar.
10. Press Reset to clear everything and observe fresh phrase predictions repopulate.

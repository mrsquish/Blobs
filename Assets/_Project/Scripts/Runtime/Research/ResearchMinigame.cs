using System;
using Blobs.Core;
using Blobs.Players;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blobs.Research
{
    public enum ResearchOutcome
    {
        Completed,
        Abandoned,
    }

    /// <summary>Everything a minigame instance gets from the 3D game. Keep this the only coupling point.</summary>
    public sealed class ResearchContext
    {
        public ResearchContext(CoopPlayer player, PhysicsScene2D physics)
        {
            Player = player;
            Physics = physics;
            Navigate = player.Input.actions.FindAction(InputNames.ResearchNavigate, throwIfNotFound: true);
            Submit = player.Input.actions.FindAction(InputNames.ResearchSubmit, throwIfNotFound: true);
            Exit = player.Input.actions.FindAction(InputNames.ResearchExit, throwIfNotFound: true);
        }

        public CoopPlayer Player { get; }
        public Color PlayerColor => Player.Color;

        /// <summary>This instance's own 2D physics world. Query it instead of the static Physics2D API.</summary>
        public PhysicsScene2D Physics { get; }

        public InputAction Navigate { get; }
        public InputAction Submit { get; }
        public InputAction Exit { get; }
    }

    /// <summary>
    /// Base class for a 2D research minigame. Several copies run at once (one per researching player), each in
    /// its own additively loaded scene with its own physics scene, so subclasses must never use singletons,
    /// static state, Time.timeScale or the global Physics2D queries.
    /// </summary>
    public abstract class ResearchMinigame : MonoBehaviour
    {
        bool _running;

        public event Action<ResearchOutcome> Finished;

        protected ResearchContext Context { get; private set; }

        /// <summary>Size of the play area in local units; the panel camera zooms to fit it.</summary>
        public abstract Vector2 ArenaSize { get; }

        /// <summary>Pauses this instance only (its clock and physics). The 3D game and other panels carry on.</summary>
        public bool IsPaused { get; set; }

        /// <summary>This instance's own clock, in seconds of unpaused play.</summary>
        protected float ElapsedTime { get; private set; }

        public void Begin(ResearchContext context)
        {
            Context = context;
            _running = true;
            OnBegin();
        }

        protected abstract void OnBegin();
        protected virtual void OnTick(float deltaTime) { }
        protected virtual void OnFixedTick(float deltaTime) { }

        protected void Finish(ResearchOutcome outcome)
        {
            if (!_running) return;
            _running = false;
            Finished?.Invoke(outcome);
        }

        void Update()
        {
            if (!_running || IsPaused) return;

            if (Context.Exit.WasPressedThisFrame())
            {
                Finish(ResearchOutcome.Abandoned);
                return;
            }

            ElapsedTime += Time.deltaTime;
            OnTick(Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (!_running || IsPaused) return;

            OnFixedTick(Time.fixedDeltaTime);
            // Scenes loaded with LocalPhysicsMode.Physics2D are never simulated automatically.
            Context.Physics.Simulate(Time.fixedDeltaTime);
        }
    }
}

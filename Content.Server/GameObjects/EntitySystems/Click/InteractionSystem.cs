﻿using Content.Server.Interfaces.GameObjects;
using SS14.Server.Interfaces.GameObjects;
using SS14.Shared.GameObjects;
using SS14.Shared.GameObjects.System;
using SS14.Shared.Interfaces.GameObjects;
using System.Collections.Generic;
using System.Linq;
using SS14.Shared.Input;
using SS14.Shared.Log;
using SS14.Shared.Map;
using SS14.Server.GameObjects;

namespace Content.Server.GameObjects.EntitySystems
{
    /// <summary>
    /// This interface gives components behavior when being clicked on or "attacked" by a user with an object in their hand
    /// </summary>
    public interface IAttackby
    {
        /// <summary>
        /// Called when using one object on another
        /// </summary>
        /// <param name="user"></param>
        /// <param name="attackwith"></param>
        /// <returns></returns>
        bool Attackby(IEntity user, IEntity attackwith);
    }

    /// <summary>
    /// This interface gives components behavior when being clicked on or "attacked" by a user with an empty hand
    /// </summary>
    public interface IAttackHand
    {
        /// <summary>
        /// Called when a player directly interacts with an empty hand
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        bool Attackhand(IEntity user);
    }

    /// <summary>
    /// This interface gives components behavior when being clicked by objects outside the range of direct use
    /// </summary>
    public interface IRangedAttackby
    {
        /// <summary>
        /// Called when we try to interact with an entity out of range
        /// </summary>
        /// <param name="user"></param>
        /// <param name="attackwith"></param>
        /// <param name="clicklocation"></param>
        /// <returns></returns>
        bool RangedAttackby(IEntity user, IEntity attackwith, GridLocalCoordinates clicklocation);
    }

    /// <summary>
    /// This interface gives components a behavior when clicking on another object and no interaction occurs
    /// Doesn't pass what you clicked on as an argument, but if it becomes necessary we can add it later
    /// </summary>
    public interface IAfterAttack
    {
        /// <summary>
        /// Called when we interact with nothing, or when we interact with an entity out of range that has no behavior
        /// </summary>
        /// <param name="user"></param>
        /// <param name="clicklocation"></param>
        /// <param name="attacked">The entity that was clicked on out of range. May be null if no entity was clicked on.true</param>
        void Afterattack(IEntity user, GridLocalCoordinates clicklocation, IEntity attacked);
    }

    /// <summary>
    /// This interface gives components behavior when using the entity in your hands
    /// </summary>
    public interface IUse
    {
        /// <summary>
        /// Called when we activate an object we are holding to use it
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        bool UseEntity(IEntity user);
    }

    /// <summary>
    /// Governs interactions during clicking on entities
    /// </summary>
    public class InteractionSystem : EntitySystem
    {
        private const float INTERACTION_RANGE = 2;
        private const float INTERACTION_RANGE_SQUARED = INTERACTION_RANGE * INTERACTION_RANGE;

        public void UserInteraction(ClickEventMessage msg, IEntity player)
        {
            //Get entity clicked upon from UID if valid UID, if not assume no entity clicked upon and null
            IEntity attacked = null;
            if (msg.Uid.IsValid())
                attacked = EntityManager.GetEntity(msg.Uid);

            //Verify player has a transform component
            if (!player.TryGetComponent<IServerTransformComponent>(out var playerTransform))
            {
                return;
            }
            //Verify player is on the same map as the entity he clicked on
            else if (msg.Coordinates.MapID != playerTransform.MapID)
            {
                Logger.Warning(string.Format("Player named {0} clicked on a map he isn't located on", player.Name));
                return;
            }

            //Verify player has a hand, and find what object he is currently holding in his active hand
            if (!player.TryGetComponent<IHandsComponent>(out var hands))
            {
                return;
            }
            var item = hands.GetActiveHand?.Owner;


            if (!MobCanInteract(player))
                return;
            //TODO: Mob status code that allows or rejects interactions based on current mob status
            //Check if client should be able to see that object to click on it in the first place, prevent using locaters by firing a laser or something


            //Clicked on empty space behavior, try using ranged attack
            if (attacked == null && item != null)
            {
                //AFTERATTACK: Check if we clicked on an empty location, if so the only interaction we can do is afterattack
                InteractAfterattack(player, item, msg.Coordinates);
                return;
            }
            else if (attacked == null)
            {
                return;
            }

            //Verify attacked object is on the map if we managed to click on it somehow
            if (!attacked.GetComponent<TransformComponent>().IsMapTransform)
            {
                Logger.Warning(string.Format("Player named {0} clicked on object {1} that isn't currently on the map somehow", player.Name, attacked.Name));
                return;
            }

            //Check if ClickLocation is in object bounds here, if not lets log as warning and see why
            if (attacked.TryGetComponent(out BoundingBoxComponent boundingbox))
            {
                if (!boundingbox.WorldAABB.Contains(msg.Coordinates.Position))
                {
                    Logger.Warning(string.Format("Player {0} clicked {1} outside of its bounding box component somehow", player.Name, attacked.Name));
                    return;
                }
            }

            //RANGEDATTACK/AFTERATTACK: Check distance between user and clicked item, if too large parse it in the ranged function
            //TODO: have range based upon the item being used? or base it upon some variables of the player himself?
            var distance = (playerTransform.WorldPosition - attacked.GetComponent<IServerTransformComponent>().WorldPosition).LengthSquared;
            if (distance > INTERACTION_RANGE_SQUARED)
            {
                if (item != null)
                {
                    RangedInteraction(player, item, attacked, msg.Coordinates);
                    return;
                }
                return; //Add some form of ranged attackhand here if you need it someday, or perhaps just ways to modify the range of attackhand
            }

            //We are close to the nearby object and the object isn't contained in our active hand
            //ATTACKBY/AFTERATTACK: We will either use the item on the nearby object
            if (item != null)
            {
                Interaction(player, item, attacked, msg.Coordinates);
            }
            //ATTACKHAND: Since our hand is empty we will use attackhand
            else
            {
                Interaction(player, attacked);
            }
        }

        /// <summary>
        /// TODO function for blocking activity based on mob status
        /// </summary>
        /// <returns></returns>
        private static bool MobCanInteract(IEntity user)
        {
            return true; //Hook into future planned mob status system
        }

        /// <summary>
        /// We didn't click on any entity, try doing an afterattack on the click location
        /// </summary>
        /// <param name="user"></param>
        /// <param name="weapon"></param>
        /// <param name="clicklocation"></param>
        public static void InteractAfterattack(IEntity user, IEntity weapon, GridLocalCoordinates clicklocation)
        {
            List<IAfterAttack> afterattacks = weapon.GetAllComponents<IAfterAttack>().ToList();

            for (var i = 0; i < afterattacks.Count; i++)
            {
                afterattacks[i].Afterattack(user, clicklocation, null);
            }
        }

        /// <summary>
        /// Uses a weapon/object on an entity
        /// Finds interactable components with the Attackby interface and calls their function
        /// </summary>
        /// <param name="user"></param>
        /// <param name="weapon"></param>
        /// <param name="attacked"></param>
        public static void Interaction(IEntity user, IEntity weapon, IEntity attacked, GridLocalCoordinates clicklocation)
        {
            List<IAttackby> interactables = attacked.GetAllComponents<IAttackby>().ToList();

            for (var i = 0; i < interactables.Count; i++)
            {
                if (interactables[i].Attackby(user, weapon)) //If an attackby returns a status completion we finish our attack
                {
                    return;
                }
            }

            //Else check damage component to see if we damage if not attackby, and if so can we attack object


            //If we aren't directly attacking the nearby object, lets see if our item has an after attack we can do
            List<IAfterAttack> afterattacks = weapon.GetAllComponents<IAfterAttack>().ToList();

            for (var i = 0; i < afterattacks.Count; i++)
            {
                afterattacks[i].Afterattack(user, clicklocation, attacked);
            }
        }

        /// <summary>
        /// Uses an empty hand on an entity
        /// Finds interactable components with the Attackhand interface and calls their function
        /// </summary>
        /// <param name="user"></param>
        /// <param name="attacked"></param>
        public static void Interaction(IEntity user, IEntity attacked)
        {
            List<IAttackHand> interactables = attacked.GetAllComponents<IAttackHand>().ToList();

            for (var i = 0; i < interactables.Count; i++)
            {
                if (interactables[i].Attackhand(user)) //If an attackby returns a status completion we finish our attack
                {
                    return;
                }
            }

            //Else check damage component to see if we damage if not attackby, and if so can we attack object
        }

        /// <summary>
        /// Activates the Use behavior of an object
        /// Verifies that the user is capable of doing the use interaction first
        /// </summary>
        /// <param name="user"></param>
        /// <param name="used"></param>
        public static void TryUseInteraction(IEntity user, IEntity used)
        {
            if (user != null && used != null && MobCanInteract(user))
            {
                UseInteraction(user, used);
            }
        }

        /// <summary>
        /// Activates/Uses an object in control/possession of a user
        /// If the item has the IUse interface on one of its components we use the object in our hand
        /// </summary>
        /// <param name="user"></param>
        /// <param name="attacked"></param>
        public static void UseInteraction(IEntity user, IEntity used)
        {
            List<IUse> usables = used.GetAllComponents<IUse>().ToList();

            //Try to use item on any components which have the interface
            for (var i = 0; i < usables.Count; i++)
            {
                if (usables[i].UseEntity(user)) //If an attackby returns a status completion we finish our attack
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Will have two behaviors, either "uses" the weapon at range on the entity if it is capable of accepting that action
        /// Or it will use the weapon itself on the position clicked, regardless of what was there
        /// </summary>
        /// <param name="user"></param>
        /// <param name="weapon"></param>
        /// <param name="attacked"></param>
        public static void RangedInteraction(IEntity user, IEntity weapon, IEntity attacked, GridLocalCoordinates clicklocation)
        {
            List<IRangedAttackby> rangedusables = attacked.GetAllComponents<IRangedAttackby>().ToList();

            //See if we have a ranged attack interaction
            for (var i = 0; i < rangedusables.Count; i++)
            {
                if (rangedusables[i].RangedAttackby(user, weapon, clicklocation)) //If an attackby returns a status completion we finish our attack
                {
                    return;
                }
            }

            if (weapon != null)
            {
                List<IAfterAttack> afterattacks = weapon.GetAllComponents<IAfterAttack>().ToList();

                //See if we have a ranged attack interaction
                for (var i = 0; i < afterattacks.Count; i++)
                {
                    afterattacks[i].Afterattack(user, clicklocation, attacked);
                }
            }
        }
    }
}

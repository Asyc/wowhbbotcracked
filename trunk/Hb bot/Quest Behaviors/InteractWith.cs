﻿// Behavior originally contributed by Nesox.
//
// DOCUMENTATION:
//     http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Custom_Behavior:_InteractWith
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.LootFrame;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;
using Action = TreeSharp.Action;


namespace Styx.Bot.Quest_Behaviors
{
    /// <summary>
    /// Allows you to do quests that requires you to interact with nearby objects.
    /// ##Syntax##
    /// [Optional]QuestId: Id of the quest.
    /// MobId1, MobId2, ...MobIdN: Id of the objects to interact with.
    /// [Optional]NumOfTimes: Number of times to interact with object.
    /// [Optional]GossipOption: The Dialog numbers you wish to choose. Should be seperated with commas. ie. GossipOption="1,1,4,2" or GossipOption="1"
    /// [Optional]CollectionDistance: The distance it will use to collect objects. DefaultValue:100 yards
    /// [Optional]BuySlot: Buys the item from the slot. Slots are: 0 1
    ///                                                            2 3
    ///                                                            4 5
    ///                                                            6 7
    ///                                                            page2
    ///                                                            8 9 etc.
    /// [Optional]BuyItemId: Buys the item with that id from vendor.
    /// [Optional]BuyItemCount: The amount to buy the item. Default: 1
    /// [Optional]WaitTime: The time to wait once it has interacted with an object. DefaultValue:3000
    /// [Optional]ObjectType: the type of object to interact with, expected value: Npc/Gameobject
    /// [Optional]X,Y,Z: The general location where theese objects can be found
    /// </summary>
    public class InteractWith : CustomForcedBehavior
    {
        public enum ObjectType
        {
            Npc,
            GameObject,
        }

        public enum NpcStateType
        {
            Alive,
            BelowHp,
            Dead,
            DontCare,
        }

        public enum NavigationType
        {
            Mesh,
            CTM,
            None,
        }
        
        public InteractWith(Dictionary<string, string> args)
            : base(args)
        {
            try
            {
                // Warn of deprecated attributes...
                if (args.ContainsKey("BuySlot"))
                {
                    LogMessage("warning",   "*****\n"
                                            + "* THE BUYSLOT ATTRIBUTE IS DEPRECATED, and may be retired in a near, future release.\n"
                                            + "*\n"
                                            + "* BuySlot presents a number of problems.  If a vendor presents 'seasonal' or\n"
                                            + "* limited-quantity wares, the slot number for the desired item can change.\n"
                                            + "\n"
                                            + "* Please update the profile to use *BuyItemId* attribute in preference to BuySlot.\n"
                                            + "*****");
                }

                // QuestRequirement* attributes are explained here...
                //    http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_QuestId_for_Custom_Behaviors
                // ...and also used for IsDone processing.
                BuyItemCount    = GetAttributeAsNullable<int>("BuyItemCount", false, ConstrainAs.CollectionCount, null) ?? 1;
                BuyItemId       = GetAttributeAsNullable<int>("BuyItemId", false, ConstrainAs.ItemId, null) ?? 0;
                BuySlot         = GetAttributeAsNullable<int>("BuySlot", false, new ConstrainTo.Domain<int>(-1, 100), null) ?? -1;
                CollectionDistance = GetAttributeAsNullable<double>("CollectionDistance", false, ConstrainAs.Range, null) ?? 100;
                GossipOptions   = GetAttributeAsArray<int>("GossipOptions", false, new ConstrainTo.Domain<int>(-1, 10), new [] { "GossipOption" }, null);
                Location        = GetAttributeAsNullable<WoWPoint>("", false, ConstrainAs.WoWPointNonEmpty, null) ?? Me.Location;
                Loot            = GetAttributeAsNullable<bool>("Loot", false, null, null) ?? false;
                MobIds          = GetNumberedAttributesAsArray<int>("MobId", 1, ConstrainAs.MobId, new [] { "NpcId" });
                ObjType         = GetAttributeAsNullable<ObjectType>("ObjectType", false, null, new [] { "MobType" }) ?? ObjectType.Npc;
                NpcState        = GetAttributeAsNullable<NpcStateType>("MobState", false, null, new[] { "NpcState" }) ?? NpcStateType.DontCare;
                NavigationState = GetAttributeAsNullable<NavigationType>("Nav", false, null, new[] { "Navigation" }) ?? NavigationType.Mesh;
                MobHpPercentLeft = GetAttributeAsNullable<double>("MobHpPercentLeft", false, ConstrainAs.Percent, new[] { "HpLeftAmount" }) ?? 100.0;
                NotMoving       = GetAttributeAsNullable<bool>("NotMoving", false, null, null) ?? false;
                NumOfTimes      = GetAttributeAsNullable<int>("NumOfTimes", false, ConstrainAs.RepeatCount, null) ?? 1;
                QuestId         = GetAttributeAsNullable("QuestId", false, ConstrainAs.QuestId(this), null) ?? 0;
                QuestRequirementComplete = GetAttributeAsNullable<QuestCompleteRequirement>("QuestCompleteRequirement", false, null, null) ?? QuestCompleteRequirement.NotComplete;
                QuestRequirementInLog    = GetAttributeAsNullable<QuestInLogRequirement>("QuestInLogRequirement", false, null, null) ?? QuestInLogRequirement.InLog;
                Range           = GetAttributeAsNullable<double>("Range", false, ConstrainAs.Range, null) ?? 4.0;
                WaitForNpcs     = GetAttributeAsNullable<bool>("WaitForNpcs", false, null, null) ?? true;
                WaitTime        = GetAttributeAsNullable<int>("WaitTime", false, ConstrainAs.Milliseconds, null) ?? 3000;
                IgnoreCombat    = GetAttributeAsNullable<bool>("IgnoreCombat", false, null, null) ?? false;

                for (int i = 0;  i < GossipOptions.Length;  ++i)
                    { GossipOptions[i] -= 1; }


                IEnumerable<WoWUnit>    mobs    = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                                                    .Where(unit => MobIds.Contains((int)unit.Entry));

                MobNames = string.Join(", ", mobs.Select(mob => (!string.IsNullOrEmpty(mob.Name)
                                                                ? mob.Name
                                                                : ("Mob(" + mob.Entry.ToString() + ")")))
                                                 .ToArray());
			}

			catch (Exception except)
			{
				// Maintenance problems occur for a number of reasons.  The primary two are...
				// * Changes were made to the behavior, and boundary conditions weren't properly tested.
				// * The Honorbuddy core was changed, and the behavior wasn't adjusted for the new changes.
				// In any case, we pinpoint the source of the problem area here, and hopefully it
				// can be quickly resolved.
				LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message
									+ "\nFROM HERE:\n"
									+ except.StackTrace + "\n");
				IsAttributeProblem = true;
			}
        }

        // Attributes provided by caller
        public int                      BuyItemCount { get; private set; }
        public int                      BuyItemId { get; private set; }
        public int                      BuySlot { get; private set; }
        public double                   CollectionDistance { get; private set; }
        public int[]                    GossipOptions { get; private set; }
        public WoWPoint                 Location { get; private set; }
        public bool                     Loot { get; private set; }
        public int[]                    MobIds { get; private set; }
        public string                   MobNames { get; private set; }
        public NpcStateType             NpcState { get; private set; }
        public NavigationType           NavigationState { get; private set; }
        public ObjectType               ObjType { get; private set; }
        public bool                     NotMoving { get; private set; }
        public int                      NumOfTimes { get; private set; }
        public int                      QuestId { get; private set; }
        public QuestCompleteRequirement QuestRequirementComplete { get; private set; }
        public QuestInLogRequirement    QuestRequirementInLog { get; private set; }
        public double                   Range { get; private set; }
        public bool                     WaitForNpcs { get; private set; }
        public int                      WaitTime { get; private set; }
        public bool                     IgnoreCombat { get; private set; }
        public double                   MobHpPercentLeft { get; private set; }

        // Private variables for internal state
        private bool                    _isBehaviorDone;
        private bool                    _isDisposed;
        private readonly List<ulong>    _npcBlacklist = new List<ulong>();
        private Composite               _root;

        // Private properties
        public int                      Counter { get; private set; }
        private LocalPlayer             Me { get { return (ObjectManager.Me); } }

        // DON'T EDIT THESE--they are auto-populated by Subversion
        public override string      SubversionId { get { return ("$Id: InteractWith.cs 199 2011-07-15 10:22:34Z chinajade $"); } }
        public override string      SubversionRevision { get { return ("$Revision: 199 $"); } }


        ~InteractWith()
        {
            Dispose(false);
        }	

		
		public void     Dispose(bool    isExplicitlyInitiatedDispose)
        {
            if (!_isDisposed)
            {
                // NOTE: we should call any Dispose() method for any managed or unmanaged
                // resource, if that resource provides a Dispose() method.

                // Clean up managed resources, if explicit disposal...
                if (isExplicitlyInitiatedDispose)
                {
                    // empty, for now
                }

                // Clean up unmanaged resources (if any) here...
                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                // Call parent Dispose() (if it exists) here ...
                base.Dispose();
            }

            _isDisposed = true;
        }


        /// <summary> Current object we should interact with.</summary>
        /// <value> The object.</value>
        private WoWObject CurrentObject
        {
            get
            {
                bool test = ObjectManager.GetObjectsOfType<WoWGameObject>().Any(delegate(WoWGameObject obj) { return obj.Entry == 191092; });
                WoWObject @object = null;
                switch (ObjType)
                {
                    case ObjectType.GameObject:
                        @object = ObjectManager.GetObjectsOfType<WoWGameObject>().OrderBy(ret => ret.Distance).FirstOrDefault(obj =>
                            !_npcBlacklist.Contains(obj.Guid) &&
                            obj.Distance < CollectionDistance &&
                            MobIds.Contains((int)obj.Entry));

                        break;

                    case ObjectType.Npc:

                        var baseTargets = ObjectManager.GetObjectsOfType<WoWUnit>()
                                                               .OrderBy(obj => obj.Distance)
                                                               .Where(obj => !_npcBlacklist.Contains(obj.Guid) &&
                                                               obj.Distance < CollectionDistance && 
                                                               !Me.Minions.Contains(obj) && 
                                                               (NotMoving ? !obj.IsMoving : true) &&
                                                                MobIds.Contains((int)obj.Entry));

                        var npcStateQualifiedTargets = baseTargets
                                                            .OrderBy(obj => obj.Distance)
                                                            .Where(target => ((NpcState == NpcStateType.DontCare)
                                                                              || ((NpcState == NpcStateType.Dead) && target.Dead)
                                                                              || ((NpcState == NpcStateType.Alive) && target.IsAlive)
                                                                              || ((NpcState == NpcStateType.BelowHp) && target.IsAlive && (target.HealthPercent < MobHpPercentLeft))));


                        @object = npcStateQualifiedTargets.FirstOrDefault();

                        break;

                }

                if (@object != null)
                    { LogMessage("debug", @object.Name); }

                return @object;
            }
        }


        #region Overrides of CustomForcedBehavior

        protected override Composite CreateBehavior()
        {
            return _root ?? (_root =
                new PrioritySelector(
                    new Decorator(ret => !_isBehaviorDone && !IsDone,
                        new PrioritySelector(
                            new Decorator(ret => Counter >= NumOfTimes,
                                new Action(ret => _isBehaviorDone = true)),

                            new PrioritySelector(

                                new Decorator(ret => CurrentObject != null && CurrentObject.Location.DistanceSqr(Me.Location) > Range * Range && NavigationState == NavigationType.Mesh,
                                    new Sequence(
                                                new Action(ret => { TreeRoot.StatusText = "Moving to interact with - " + CurrentObject.Name; }),
                                                new Action(ret => Navigator.MoveTo(CurrentObject.Location))
                                        )
                                    ),

                            new Decorator(ret => CurrentObject != null && CurrentObject.Location.DistanceSqr(Me.Location) > Range * Range && NavigationState == NavigationType.CTM,
                                    new Sequence(
                                                new Action(ret => { TreeRoot.StatusText = "Moving to interact with - " + CurrentObject.Name; }),
                                                new Action(ret => WoWMovement.ClickToMove(CurrentObject.Location))
                                        )
                                    ),

                            new Decorator(ret => CurrentObject != null && CurrentObject.Location.DistanceSqr(Me.Location) > Range * Range && NavigationState == NavigationType.None,
                                    new Sequence(
                                                new Action(ret => { TreeRoot.StatusText = "Object is out of range, Skipping - " + CurrentObject.Name + " Distance: " + CurrentObject.Distance; }),
                                                new Action(ret => _isBehaviorDone = true)
                                        )
                                    ),

                                new Decorator(ret => CurrentObject != null && CurrentObject.Location.DistanceSqr(Me.Location) <= Range * Range,
                                    new Sequence(
                                        new DecoratorContinue(ret => StyxWoW.Me.IsMoving,
                                            new Action(ret =>
                                                {
                                                    WoWMovement.MoveStop();
                                                    StyxWoW.SleepForLagDuration();
                                                })),

                                        new Action(ret =>
                                        {
                                            TreeRoot.StatusText = "Interacting with - " + CurrentObject.Name;
                                            CurrentObject.Interact();
                                            _npcBlacklist.Add(CurrentObject.Guid);

                                            Thread.Sleep(2000);
                                            Counter++;
                                        }),

                                        new DecoratorContinue(
                                            ret => GossipOptions.Length > 0,
                                            new Action(ret =>
                                                {
                                                    foreach (var gos in GossipOptions)
                                                    {
                                                        GossipFrame.Instance.SelectGossipOption(gos);
                                                        Thread.Sleep(1000);
                                                    }
                                                })),

                                        new DecoratorContinue(
                                            ret => Loot && LootFrame.Instance.IsVisible,
                                            new Action(ret => LootFrame.Instance.LootAll())),
                                    
                                        new DecoratorContinue(
                                            ret => BuyItemId != 0 && MerchantFrame.Instance.IsVisible,
                                            new Action(ret =>
                                                {
                                                    var items = MerchantFrame.Instance.GetAllMerchantItems();
                                                    var item = items.FirstOrDefault(i => i.ItemId == BuyItemId && (i.BuyPrice * (ulong)BuyItemCount) <= Me.Copper && (i.NumAvailable >= BuyItemCount || i.NumAvailable == -1));

                                                    if (item != null)
                                                    {
                                                        MerchantFrame.Instance.BuyItem(item.Index, BuyItemCount);
                                                        Thread.Sleep(1500);
                                                    }
                                                })),

                                        new DecoratorContinue(
                                            ret => BuySlot != -1 && BuyItemId == 0 && MerchantFrame.Instance.IsVisible,
                                            new Action(ret =>
                                            {
                                                var item = MerchantFrame.Instance.GetMerchantItemByIndex(BuySlot);
                                                if (item != null && (item.BuyPrice * (ulong)BuyItemCount) <= Me.Copper && (item.NumAvailable >= BuyItemCount || item.NumAvailable == -1))
                                                {
                                                    MerchantFrame.Instance.BuyItem(BuySlot, BuyItemCount);
                                                    Thread.Sleep(1500);
                                                }
                                            })),
                                        new DecoratorContinue(
                                            ret => Me.CurrentTarget != null && Me.CurrentTarget == CurrentObject,
                                            new Action(ret => Me.ClearTarget())),

                                        new Action(ret => Thread.Sleep(WaitTime))

                                    )),

                                new Decorator(
                                    ret => Location.DistanceSqr(Me.Location) > 2 * 2,
                                    new Sequence(
                                        new Action(ret => { TreeRoot.StatusText = "Moving towards - " + Location; }),
                                        new Action(ret => Navigator.MoveTo(Location)))),

                                new Decorator(
                                    ret => !WaitForNpcs && CurrentObject == null,
                                    new Action(ret => _isBehaviorDone = true)),

                                new Action(ret => TreeRoot.StatusText = "Waiting for object to spawn")

                        )))));
        }


        public override void    Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public override bool    IsDone
        {
            get
            {
                return (_isBehaviorDone     // normal completion
                        || !UtilIsProgressRequirementsMet(QuestId, QuestRequirementInLog, QuestRequirementComplete));
            }
        }


        public override void OnStart()
        {
            // This reports problems, and stops BT processing if there was a problem with attributes...
            // We had to defer this action, as the 'profile line number' is not available during the element's
            // constructor call.
            OnStart_HandleAttributeProblem();

            // If the quest is complete, this behavior is already done...
            // So we don't want to falsely inform the user of things that will be skipped.
            if (!IsDone)
            {
                TreeRoot.GoalText = "Interacting with " + MobNames;
            }

            if (IgnoreCombat && TreeRoot.Current != null && TreeRoot.Current.Root != null && TreeRoot.Current.Root.LastStatus != RunStatus.Running)
            {
                var currentRoot = TreeRoot.Current.Root;
                if (currentRoot is GroupComposite)
                {
                    var root = (GroupComposite)currentRoot;
                    root.InsertChild(0, CreateBehavior());
                }
            }
        }

        #endregion
    }
}
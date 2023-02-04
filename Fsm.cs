using System;
using System.Collections;
using System.Collections.Generic;

public class Fsm
{
    public delegate void StateEnter(int lastState, object arg);
    public delegate void StateUpdate();
    public delegate void StateExit(int newState);
    public delegate void StateTimerEnd();
    public delegate bool StateTransition(object arg);

    public class StateInfo
    {
        public int identifier;
        public List<TransitionInfo> transitionList;

        public StateEnter onEnter;
        public StateUpdate onUpdate;
        public StateExit onExit;


        public StateInfo()
        {
            transitionList = new(8);
        }

        public StateInfo SetOnEnter(StateEnter onEnter)
        {
            this.onEnter = onEnter;
            return this;
        }

        public StateInfo AddOnEnter(StateEnter onEnter)
        {
            this.onEnter += onEnter;
            return this;
        }

        public StateInfo SetOnUpdate(StateUpdate onUpdate)
        {
            this.onUpdate = onUpdate;
            return this;
        }

        public StateInfo AddOnUpdate(StateUpdate onUpdate)
        {
            this.onUpdate += onUpdate;
            return this;
        }

        public StateInfo SetOnExit(StateExit onExit)
        {
            this.onExit = onExit;
            return this;
        }

        public StateInfo AddOnExit(StateExit onExit)
        {
            this.onExit += onExit;
            return this;
        }
    }

    public class TransitionInfo
    {
        public StateInfo selfState;
        public StateInfo dstState;
        public StateTransition condition;
        public bool isAutoDetect;

        public TransitionInfo SetTransition(StateTransition condition, bool autoDetect = false)
        {
            this.condition = condition;
            this.isAutoDetect = autoDetect;

            return this;
        }
    }

    public struct FastFsmTransitionOperate
    {
        public TransitionInfo transition;
        public object transitionArg;


        public void Deconstruct(out TransitionInfo transition, out object arg)
        {
            transition = this.transition;
            arg = this.transitionArg;
        }
    }

    bool mNestedLock;

    Queue<FastFsmTransitionOperate> mNestedTransitionQueue;
    FastFsmTransitionOperate? mTransitionQuest;

    List<StateInfo> mStateList;

    StateInfo mCurrentState;
    public int CurrentStateIdentifier => mCurrentState.identifier;


    public StateInfo this[int stateIdentifier]
    {
        get
        {
            if (!HasState(stateIdentifier))
                AddState(stateIdentifier);

            return mStateList.Find(m => m.identifier == stateIdentifier);
        }
    }

    public TransitionInfo this[int stateIdentifier, int dstStateIdentifier]
    {
        get
        {
            if (!HasState(stateIdentifier))
                AddState(stateIdentifier);

            if (!HasState(dstStateIdentifier))
                AddState(dstStateIdentifier);

            if (!HasTransition(stateIdentifier, dstStateIdentifier))
                AddTransition(stateIdentifier, dstStateIdentifier, null);

            return GetTransition(stateIdentifier, dstStateIdentifier);
        }
    }


    public Fsm()
    {
        mStateList = new(32);
        mNestedTransitionQueue = new(4);
    }

    public bool HasState(int identifier)
    {
        var state = mStateList.Find(m => m.identifier == identifier);
        return state != null;
    }

    public bool HasTransition(int identifier, int dstIdentifier)
    {
        var state = mStateList.Find(m => m.identifier == identifier);
        if (state != null)
        {
            var transition = state.transitionList.Find(m => m.dstState.identifier == dstIdentifier);
            return transition != null;
        }

        return false;
    }

    public bool Start(int identifier)
    {
        var state = mStateList.Find(m => m.identifier == identifier);
        if (state == null) return false;

        mCurrentState = state;
        state.onEnter?.Invoke(-1, null);
        return true;
    }

    public void AddState(int identifier, StateEnter onEnter = null, StateUpdate onUpdate = null, StateExit onExit = null)
    {
        mStateList.Add(new StateInfo()
        {
            identifier = identifier,
            onEnter = onEnter,
            onUpdate = onUpdate,
            onExit = onExit
        });
    }

    public void AddTransition(int stateIdentifier, int dstStateIdentifier, StateTransition condition)
    {
        var state = mStateList.Find(m => m.identifier == stateIdentifier);
        var dstState = mStateList.Find(m => m.identifier == dstStateIdentifier);

        state.transitionList.Add(new TransitionInfo()
        {
            condition = condition,
            selfState = state,
            dstState = dstState
        });
    }

    public TransitionInfo GetTransition(int stateIdentifier, int dstStateIdentifier)
    {
        var stateInfo = mStateList.Find(m => m.identifier == stateIdentifier);
        if (stateInfo == null) return null;
        return stateInfo.transitionList.Find(m => m.dstState.identifier == dstStateIdentifier);
    }

    public void Transition(int dstStateIdentifier, object arg = null, bool immediateUpdate = true)
    {
        var dstTransition = mCurrentState.transitionList
            .Find(m => m.dstState.identifier == dstStateIdentifier);

        TransitionInternal(new FastFsmTransitionOperate()
        {
            transition = dstTransition,
            transitionArg = arg
        }, immediateUpdate);
    }

    public void Update(bool isFrameStep)
    {
        if (mNestedLock)
            throw new System.NotSupportedException("Does not support update nested update fsm!");

        mNestedLock = true;

        if (mTransitionQuest.HasValue)
        {
            var (transition, transitionArg) = mTransitionQuest.Value;

            if (transition.condition(transitionArg))
            {
                transition.selfState.onExit?.Invoke(transition.dstState.identifier);
                transition.dstState.onEnter?.Invoke(transition.selfState.identifier, transitionArg);

                mCurrentState = transition.dstState;
            }

            mTransitionQuest = null;
        }
        else
        {
            var state = mCurrentState;

            var transitionList = state.transitionList;
            for (int i = 0; i < transitionList.Count; i++)
            {
                var transition = transitionList[i];

                if (transition.isAutoDetect)
                {
                    TransitionInternal(new FastFsmTransitionOperate()
                    {
                        transition = transition,
                        transitionArg = null
                    }, false);
                }
            }
        }

        if (isFrameStep)
        {
            mCurrentState.onUpdate?.Invoke();
        }

        mNestedLock = false;

        while (mNestedTransitionQueue.TryDequeue(out FastFsmTransitionOperate operate))
        {
            mTransitionQuest = operate;
            Update(false);
        }
    }

    void TransitionInternal(FastFsmTransitionOperate operate, bool immediateUpdate)
    {
        if (mNestedLock)
            mNestedTransitionQueue.Enqueue(operate);
        else
            mTransitionQuest = operate;

        if (immediateUpdate)
            Update(false);
    }
}
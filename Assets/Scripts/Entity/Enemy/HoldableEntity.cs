﻿using UnityEngine;

using Fusion;
using NSMB.Utils;

public abstract class HoldableEntity : KillableEntity {

    //---Networked Variables
    [Networked] public PlayerController Holder { get; set; }
    [Networked] public PlayerController PreviousHolder { get; set; }
    [Networked] protected TickTimer ThrowInvincibility { get; set; }

    //---Serailized Variables
    [SerializeField] protected float throwSpeed = 4.5f;

    //--Misc Variables
    public Vector3 holderOffset;
    public bool canPlace = true, canKick = true;

    #region Unity Methods
    public override void FixedUpdateNetwork() {
        if (!Holder) {
            base.FixedUpdateNetwork();
        } else {
            body.velocity = Vector2.zero;
            body.position = Holder.body.position + (Vector2) holderOffset;
            sRenderer.flipX = !FacingRight;
        }
    }
    #endregion

    #region Implemented Methods
    public abstract void Kick(bool fromLeft, float kickFactor, bool groundpound);

    public virtual void Throw(bool toRight, bool crouching) {
        if (!Holder)
            return;

        if (Utils.IsTileSolidAtWorldLocation(body.position))
            transform.position = body.position = new(Holder.transform.position.x, transform.position.y);

        ThrowInvincibility = TickTimer.CreateFromSeconds(Runner, 0.2f);
        PreviousHolder = Holder;
        Holder = null;
        FacingRight = toRight;

        body.velocity = new Vector2((crouching && canPlace ? 2f : throwSpeed) * (FacingRight ? 1 : -1), body.velocity.y);
    }

    public virtual void Pickup(PlayerController player) {
        if (Holder)
            return;

        player.SetHolding(this);
    }

    public override void InteractWithPlayer(PlayerController player) {
        //don't interact with our lovely holder
        if (Holder == player)
            return;

        //temporary invincibility
        if (PreviousHolder == player && !ThrowInvincibility.ExpiredOrNotRunning(Runner))
            return;

        base.InteractWithPlayer(player);
    }

    public override void Kill() {
        if (Dead)
            return;

        if (Holder)
            Holder.SetHolding(null);

        base.Kill();
    }

    public override void SpecialKill(bool right, bool groundpound, int combo) {
        if (Dead)
            return;

        if (Holder)
            Holder.SetHolding(null);

        base.SpecialKill(right, groundpound, combo);
    }

    #endregion
}

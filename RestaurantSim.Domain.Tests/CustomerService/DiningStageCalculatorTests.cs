using FluentAssertions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using Xunit;

namespace RestaurantSim.Domain.Tests.CustomerService;

public class DiningStageCalculatorTests
{
    [Fact]
    public void LeavingGroup_IsLeaving_WhateverElseIsSet()
    {
        var group = Group(GroupPhaseWaitState.Serving, DiningVisit.Bill, GroupLifecycleState.Leaving);

        DiningStageCalculator.Calculate(group, OrderWith(OrderItemStatus.Queued)).Should().Be(DiningStage.Leaving);
    }

    [Theory]
    [InlineData(DiningVisit.Drinks)]
    [InlineData(DiningVisit.Main)]
    [InlineData(DiningVisit.Refill)]
    [InlineData(DiningVisit.Dessert)]
    public void WaiterVisitScheduled_IsOrdering(DiningVisit visit)
    {
        var group = Group(GroupPhaseWaitState.AutoVisitPending, visit);

        DiningStageCalculator.Calculate(group, null).Should().Be(DiningStage.Ordering);
    }

    [Fact]
    public void RecheckScheduled_IsStillOrdering()
    {
        var group = Group(GroupPhaseWaitState.RecheckScheduled, DiningVisit.Main);

        DiningStageCalculator.Calculate(group, null).Should().Be(DiningStage.Ordering);
    }

    [Fact]
    public void AwaitingDecision_IsOrdering()
    {
        var group = Group(GroupPhaseWaitState.AwaitingDecision, DiningVisit.Dessert);

        DiningStageCalculator.Calculate(group, null).Should().Be(DiningStage.Ordering);
    }

    [Theory]
    [InlineData(GroupPhaseWaitState.AutoVisitPending)]
    [InlineData(GroupPhaseWaitState.RecheckScheduled)]
    public void BillVisitScheduled_IsPaying(GroupPhaseWaitState wait)
    {
        var group = Group(wait, DiningVisit.Bill);

        DiningStageCalculator.Calculate(group, OrderWith(OrderItemStatus.Served)).Should().Be(DiningStage.Paying);
    }

    [Fact]
    public void NextVisitIsBillButDessertIsStillBeingMade_IsWaitingForFood_NotPaying()
    {
        // TakeOrder(Dessert) sets NextVisit = Bill immediately; the group is not paying yet.
        var group = Group(GroupPhaseWaitState.Serving, DiningVisit.Bill);

        DiningStageCalculator.Calculate(group, OrderWith(OrderItemStatus.Served, OrderItemStatus.Cooking))
            .Should().Be(DiningStage.WaitingForFood);
    }

    [Theory]
    [InlineData(OrderItemStatus.Queued)]
    [InlineData(OrderItemStatus.Cooking)]
    [InlineData(OrderItemStatus.HeldUnderLamp)]
    [InlineData(OrderItemStatus.Ready)]
    public void ServingWithAnItemNotYetServed_IsWaitingForFood(OrderItemStatus unserved)
    {
        var group = Group(GroupPhaseWaitState.Serving, DiningVisit.Main);

        DiningStageCalculator.Calculate(group, OrderWith(OrderItemStatus.Served, unserved))
            .Should().Be(DiningStage.WaitingForFood);
    }

    [Fact]
    public void ServingWithEverythingServed_IsEating()
    {
        var group = Group(GroupPhaseWaitState.Serving, DiningVisit.Main);

        DiningStageCalculator.Calculate(group, OrderWith(OrderItemStatus.Served, OrderItemStatus.Served))
            .Should().Be(DiningStage.Eating);
    }

    [Fact]
    public void ServingWithNoOrder_IsEating()
    {
        var group = Group(GroupPhaseWaitState.Serving, DiningVisit.Main);

        DiningStageCalculator.Calculate(group, null).Should().Be(DiningStage.Eating);
    }

    private static CustomerGroup Group(
        GroupPhaseWaitState wait, DiningVisit visit, GroupLifecycleState lifecycle = GroupLifecycleState.Seated) => new()
    {
        GroupId = 1, PartySize = 2, ArrivalTick = 0, Members = new List<Customer>(),
        LifecycleState = lifecycle, PhaseWaitState = wait, NextVisit = visit
    };

    private static Order OrderWith(params OrderItemStatus[] statuses) => new()
    {
        OrderId = 1, GroupId = 1, ClusterId = 1, CreatedAtTick = 0,
        Items = statuses.Select((s, i) => new OrderItem
        {
            OrderItemId = i + 1, OwnerId = 1, MenuItemId = 1, Phase = ItemPhase.EntryMain,
            PriceAtOrder = 1m, Status = s, CreatedAtTick = 0
        }).ToList()
    };
}

﻿using System;
using System.Collections.Generic;
using Marten.Events.Projections;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class project_events_from_multiple_streams_into_view : DocumentSessionFixture<IdentityMap>
    {
        static readonly Guid streamId = Guid.NewGuid();

        QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
        QuestEnded ended = new QuestEnded { Id = streamId, Name = "Find the Orb" };
        MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
        MonsterSlayed slayed1 = new MonsterSlayed { QuestId = streamId, Name = "Troll" };
        MonsterSlayed slayed2 = new MonsterSlayed { QuestId = streamId, Name = "Dragon" };
        MonsterDestroyed destroyed = new MonsterDestroyed { QuestId = streamId, Name = "Troll" };
        MembersDeparted departed = new MembersDeparted { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };
        MembersJoined joined2 = new MembersJoined { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };

        [Fact]
        public void from_configuration()
        {
            var events = new List<object>();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEvent<QuestStarted>((view, @event) => events.Add(@event))
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => events.Add(@event))
                    .ProjectEvent<MonsterSlayed>(e => e.QuestId, (view, @event) => events.Add(@event));
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            theSession.SaveChanges();

            theSession.Events.Append(streamId, joined2);
            theSession.SaveChanges();

            events.Count.ShouldBe(5);
            events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public async void from_configuration_async()
        {
            var events = new List<object>();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEvent<QuestStarted>((view, @event) => { events.Add(@event);})
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => { events.Add(@event); })
                    .ProjectEvent<MonsterSlayed>(e => e.QuestId, (view, @event) => { events.Add(@event); });
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            await theSession.SaveChangesAsync();

            theSession.Events.Append(streamId, joined2);
            await theSession.SaveChangesAsync();

            events.Count.ShouldBe(5);
            events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public void from_projection()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistViewProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            theSession.SaveChanges();

            theSession.Events.Append(streamId, joined2);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }

        [Fact]
        public async void from_projection_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistViewProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            await theSession.SaveChangesAsync();

            theSession.Events.Append(streamId, joined2);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }
    }

    public class PersistedView
    {
        public Guid Id { get; set; }
        public List<object> Events { get; } = new List<object>();
    }

    public class PersistViewProjection : ViewProjection<PersistedView, Guid>
    {
        public PersistViewProjection()
        {
            ProjectEvent<QuestStarted>(Persist);
            ProjectEvent<MembersJoined>(e => e.QuestId, Persist);
            ProjectEvent<MonsterSlayed>((session, e) => session.Load<QuestParty>(e.QuestId).Id, Persist);
            DeleteEvent<QuestEnded>();
            DeleteEvent<MembersDeparted>(e => e.QuestId);
            DeleteEvent<MonsterDestroyed>((session, e) => session.Load<QuestParty>(e.QuestId).Id);
        }

        private void Persist<T>(PersistedView view, T @event)
        {
            view.Events.Add(@event);
        }
    }
}
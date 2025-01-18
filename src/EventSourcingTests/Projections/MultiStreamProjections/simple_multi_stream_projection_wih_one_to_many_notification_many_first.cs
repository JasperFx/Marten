using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiStreamProjections.Samples
{
    #region sample_view-projection-simple-with-one-to-many

    public class UserNotificationProjection: MultiStreamProjection<UserNotification, string>
    {
        public UserNotificationProjection()
        {
            Identity<UserOpenedNotification>(x => x.UserId);
            Identities<SendNotificationToUsers>(x => x.UserIds);
        }

        public void Apply(UserOpenedNotification @event, UserNotification view)
        {
            view.isOpened = true;
        }

        public void Apply(SendNotificationToUsers @event, UserNotification view)
        {
            Assert.NotNull(view.Id); //! Fails
            view.Id = $"{view.Id}:{@event.Notification.Id}"; // I want the userNotification id to be "userId:notificationId", but since the matched userId is not available here, I can't do that.
            view.NotificationId = @event.Notification.Id;
        }
    }

    #endregion
}

namespace EventSourcingTests.Projections.MultiStreamProjections
{


    public class simple_multi_stream_projection_with_one_to_many_notification_many_first: OneOffConfigurationsContext
    {
        [Fact]
        public async Task multi_stream_projections_with_only_group_assign_should_work()
        {
            // Create a notification and send it to John and Anna
            var notification = new Notification(Guid.NewGuid().ToString(), "Notification title", "Example content");

            var johnUserId = Guid.NewGuid().ToString(); // UserId of John
            var annaUserId = Guid.NewGuid().ToString(); // UserId of Anna

            var sendNotificationToUsers = new SendNotificationToUsers(notification, new List<string> { johnUserId, annaUserId });
            theSession.Events.Append(sendNotificationToUsers.Notification.Id, sendNotificationToUsers);

            await theSession.SaveChangesAsync(); //! FAILS because of inner assert

            // Open the notification for John
            var userOpenedNotification = new UserOpenedNotification(notification.Id, johnUserId);
            theSession.Events.Append(userOpenedNotification.UserId, userOpenedNotification);

            await theSession.SaveChangesAsync();

            // Check if the notification is opened for John
            var userNotification = await theSession.LoadAsync<UserNotification>($"{johnUserId}:{notification.Id}");
            userNotification.isOpened.ShouldBeTrue();

            // Check if the notification is not opened for Anna
            userNotification = await theSession.LoadAsync<UserNotification>($"{annaUserId}:{notification.Id}");
            userNotification.isOpened.ShouldBeFalse();
        }

        public simple_multi_stream_projection_with_one_to_many_notification_many_first()
        {
            StoreOptions(_ =>
            {
                _.Events.StreamIdentity = Marten.Events.StreamIdentity.AsString;
                _.Projections.Add<EventSourcingTests.Projections.MultiStreamProjections.Samples.UserNotificationProjection>(ProjectionLifecycle.Inline);
            });
        }
    }
}

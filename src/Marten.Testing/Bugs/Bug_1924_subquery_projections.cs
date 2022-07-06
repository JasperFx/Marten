using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1924_subquery_projections : BugIntegrationContext
    {
        [Fact]
        public async Task Should_be_able_project_scalar_entire_objects_from_a_collection()
        {
            var userId = Guid.NewGuid();
            var roleId1 = Guid.NewGuid();
            var roleId2 = Guid.NewGuid();

            var insertSession = theStore.LightweightSession();
            await using (insertSession)
            {
                var user = new Bug1924User
                {
                    Id = userId,
                    RoleIds = new List<Guid> { roleId1 },
                    EmbeddedUser = new Bug1924User { Id = Guid.NewGuid(), RoleIds = new List<Guid> { roleId2 } }
                };
                insertSession.Store(user);
                await insertSession.SaveChangesAsync();
            }

            var retrievedUser = await theSession
                .Query<Bug1924User>()
                .Select(x => new { x.Id, x.RoleIds })
                .Select(x => new
                {
                    x.Id,
                    FirstRoleId = x.RoleIds.FirstOrDefault()
                })
                .SingleAsync();

            retrievedUser.Id.ShouldBe(userId);
            retrievedUser.FirstRoleId.ShouldBe(roleId1);
        }
    }

    public class Bug1924User
    {
        public Guid Id { get; set; }
        public List<Guid> RoleIds { get; set; }
        public Bug1924User EmbeddedUser { get; set; }
    }
}

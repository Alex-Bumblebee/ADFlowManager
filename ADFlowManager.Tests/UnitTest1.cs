namespace ADFlowManager.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void ApplicationModels_CanBeInstantiated()
        {
            var user = new Core.Models.User();
            Assert.NotNull(user);
            Assert.Equal("", user.UserName);

            var group = new Core.Models.Group();
            Assert.NotNull(group);
            Assert.Equal("", group.GroupName);

            var template = new Core.Models.UserTemplate();
            Assert.NotNull(template);
            Assert.NotNull(template.Groups);

            var settings = new Core.Models.AppSettings();
            Assert.NotNull(settings);
            Assert.NotNull(settings.ActiveDirectory);
            Assert.NotNull(settings.Cache);
        }

        [Fact]
        public void AuditActionTypes_AreNotEmpty()
        {
            Assert.False(string.IsNullOrWhiteSpace(Core.Models.AuditActionType.CreateUser));
            Assert.False(string.IsNullOrWhiteSpace(Core.Models.AuditActionType.UpdateUser));
            Assert.False(string.IsNullOrWhiteSpace(Core.Models.AuditActionType.ResetPassword));
        }
    }
}

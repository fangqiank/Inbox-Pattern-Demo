namespace OutboxPatternDemo.Domain
{
    public class User : Entity
    {
        public Guid Id { get; private set; }
        public string Username { get; private set; } = string.Empty;
        public List<UserFollow> Following { get; private set; } = [];

        private User()
        {
            
        }

        public User(Guid id, string username)
        {
            Id = id;
            Username = username;
        }

        public void Follow(User userToFollow)
        {
            if (Id == userToFollow.Id)
                throw new InvalidOperationException("Cannot follow yourself");

            if (Following.Any(f => f.FollowedId == userToFollow.Id))
                throw new InvalidOperationException("Already following this user");

            Following.Add(new UserFollow(Id, userToFollow.Id));
            AddDomainEvent(new UserFollowedEvent(Id, userToFollow.Id));
        }
    }

    public class UserFollow
    {
        public Guid FollowerId { get; private set; }
        public Guid FollowedId { get; private set; }
        public DateTimeOffset FollowedOn { get; private set; }

        private UserFollow()
        {
            
        }
        public UserFollow(Guid followerId, Guid followedId)
        {
            FollowerId = followerId;
            FollowedId = followedId;
            FollowedOn = DateTimeOffset.UtcNow;
        }
    }
}

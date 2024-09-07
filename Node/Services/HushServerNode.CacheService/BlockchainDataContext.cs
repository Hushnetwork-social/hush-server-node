using HushServerNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HushServerNode.CacheService
{
    public class BlockchainDataContext : BaseDbContext
    {
        public DbSet<BlockchainState> BlockchainState { get; set; }

        public DbSet<BlockEntity> BlockEntities { get; set; }

        public DbSet<AddressBalance> AddressesBalance { get; set; }

        public DbSet<Profile> Profiles { get; set; }

        public DbSet<FeedEntity> FeedEntities { get; set; }

        public DbSet<FeedParticipants> FeedParticipants { get; set; }

        public DbSet<FeedMessageEntity> FeedMessages { get; set; }
        private readonly CacheServiceConfigurator _cacheServiceConfigurator;

        public BlockchainDataContext(
            CacheServiceConfigurator cacheServiceConfigurator,
            IConfiguration configuration) : base(configuration) 
        { 
            this._cacheServiceConfigurator = cacheServiceConfigurator;   
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            this._cacheServiceConfigurator.Configure(modelBuilder);

            // modelBuilder
            //     .Entity<BlockchainState>()
            //     .ToTable("BlockchainState")
            //     .HasKey(x => x.BlockchainStateId);

            // modelBuilder
            //     .Entity<BlockEntity>()
            //     .ToTable("BlockEntity")
            //     .HasKey(x => x.BlockId);

            // modelBuilder
            //     .Entity<BlockEntity>()
            //     .Property(x => x.BlockJson)
            //     .HasColumnType("jsonb")
            //     .IsRequired();

            // modelBuilder
            //     .Entity<AddressBalance>()
            //     .ToTable("AddressBalance")
            //     .HasKey(x => x.Address);

            // modelBuilder
            //     .Entity<Profile>()
            //     .ToTable("Profile")
            //     .HasKey(x => x.PublicSigningAddress);

            // modelBuilder
            //     .Entity<FeedEntity>()
            //     .ToTable("FeedEntity")
            //     .HasKey(x => x.FeedId);
            // modelBuilder
            //     .Entity<FeedEntity>()
            //     .HasMany(x => x.FeedParticipants)
            //     .WithOne(x => x.Feed)
            //     .HasForeignKey(x => x.FeedId);

            // modelBuilder
            //     .Entity<FeedParticipants>()
            //     .ToTable("FeedParticipants")
            //     .HasKey(x => new 
            //     {
            //         x.FeedId,
            //         x.ParticipantPublicAddress
            //     });

            // modelBuilder
            //     .Entity<FeedMessageEntity>()
            //     .ToTable("FeedMessageEntity")
            //     .HasKey(x => x.FeedMessageId);

            base.OnModelCreating(modelBuilder);
        }
    }
}
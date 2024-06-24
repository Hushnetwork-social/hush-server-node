using System;
using HushServerNode.ApplicationSettings;
using Microsoft.EntityFrameworkCore;

namespace HushServerNode.CacheService
{
    public class BlockchainDataContext : DbContext
    {
        public DbSet<BlockchainState> BlockchainState { get; set; }

        private readonly IApplicationSettingsService _applicationSettingsService;

        public BlockchainDataContext(IApplicationSettingsService applicationSettingsService)
        {
            this._applicationSettingsService = applicationSettingsService;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!string.IsNullOrEmpty(this._applicationSettingsService.ConnectionString))
            {
                optionsBuilder.UseNpgsql(this._applicationSettingsService.ConnectionString);
            }
            else
            {
                throw new InvalidOperationException($"Cannot connect to local database with connection string: {this._applicationSettingsService.ConnectionString}.");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<BlockchainState>()
                .ToTable("BlockchainState")
                .HasKey(x => x.BlockchainStateId);

            base.OnModelCreating(modelBuilder);
        }
    }
}
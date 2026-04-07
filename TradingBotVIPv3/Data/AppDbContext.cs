using Microsoft.EntityFrameworkCore;
using TradingBotVIPv3.Data.Models;

namespace TradingBotVIPv3.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<VipChannel> VipChannels => Set<VipChannel>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<TraderChannel> TraderChannels => Set<TraderChannel>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<User>().HasIndex(u => u.TelegramId).IsUnique();
        mb.Entity<AdminUser>().HasIndex(u => u.TelegramId).IsUnique();
        mb.Entity<Setting>().HasKey(s => s.Key);

        mb.Entity<Subscription>()
            .HasOne(s => s.User).WithMany(u => u.Subscriptions)
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Subscription>()
            .HasOne(s => s.Plan).WithMany(p => p.Subscriptions)
            .HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Payment>()
            .HasOne(p => p.User).WithMany(u => u.Payments)
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

        mb.Entity<Payment>()
            .HasOne(p => p.Plan).WithMany(p => p.Payments)
            .HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.SetNull);

        mb.Entity<Plan>()
            .HasOne(p => p.VipChannel).WithMany(v => v.Plans)
            .HasForeignKey(p => p.VipChannelId).OnDelete(DeleteBehavior.Restrict);
    }
}
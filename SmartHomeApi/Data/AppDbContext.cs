using Microsoft.EntityFrameworkCore;
using SmartHomeApi.Models;

namespace SmartHomeApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<DeviceShare> DeviceShares { get; set; }
}
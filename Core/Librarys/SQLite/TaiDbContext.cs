using Core.Models;
using Core.Models.Db;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Librarys.SQLite
{
    public class TaiDbContext : DbContext
    {
        /// <summary>
        /// 每日数据
        /// </summary>
        public DbSet<DailyLogModel> DailyLog { get; set; }
        /// <summary>
        /// 时段数据
        /// </summary>
        public DbSet<HoursLogModel> HoursLog { get; set; }
        public DbSet<AppModel> App { get; set; }
        /// <summary>
        /// 分类
        /// </summary>
        public DbSet<CategoryModel> Categorys { get; set; }
        /// <summary>
        /// 网站
        /// </summary>
        public DbSet<WebSiteModel> WebSites { get; set; }
        /// <summary>
        /// 网站分类
        /// </summary>
        public DbSet<WebSiteCategoryModel> WebSiteCategories { get; set; }
        /// <summary>
        /// 网页浏览记录（每小时）
        /// </summary>
        public DbSet<WebBrowseLogModel> WebBrowserLogs { get; set; }
        /// <summary>
        /// 网页链接
        /// </summary>
        public DbSet<WebUrlModel> WebUrls { get; set; }

        private static string _dbFilePath = Path.Combine(FileHelper.GetRootDirectory(), "Data", "data.db");
        private static bool _walInitialized = false;
        private static readonly object _walLock = new object();

        public TaiDbContext()
       : base(new SQLiteConnection()
       {
           ConnectionString = $"Data Source={_dbFilePath}",
           BusyTimeout = 5000
       }, true)
        {
            DbConfiguration.SetConfiguration(new SQLiteConfiguration());
            EnsureWalMode();
        }

        /// <summary>
        /// Enables WAL journal mode once per process. WAL allows concurrent
        /// readers and writers, eliminating the "database is locked" crashes
        /// (upstream issue #223). Uses a static flag so the PRAGMA only fires
        /// on the first connection.
        /// </summary>
        private void EnsureWalMode()
        {
            if (_walInitialized) return;
            lock (_walLock)
            {
                if (_walInitialized) return;
                try
                {
                    Database.Connection.Open();
                    using (var cmd = Database.Connection.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA journal_mode=WAL;";
                        cmd.ExecuteNonQuery();
                    }
                    Database.Connection.Close();
                    _walInitialized = true;
                }
                catch
                {
                    // Non-fatal: if WAL setup fails (e.g. read-only media),
                    // fall back to default journal mode.
                }
            }
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            var model = modelBuilder.Build(Database.Connection);
            new SQLiteBuilder(model).SelfCheck();
        }

        public void SelfCheck()
        {
            Database.ExecuteSqlCommand("select count(*) from sqlite_master where type='table' and name='tai'");
        }
    }
}
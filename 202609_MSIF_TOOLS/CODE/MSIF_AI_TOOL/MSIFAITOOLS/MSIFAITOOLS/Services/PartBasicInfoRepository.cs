using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部品基本信息表的 SQLite 仓储。
    /// 数据库文件默认位于 &lt;程序目录&gt;\Data\MSIF_Parts.db，
    /// 可用 App.config 的 PartDatabasePath 覆盖（相对路径按程序目录解析）。
    /// </summary>
    internal sealed class PartBasicInfoRepository : IDisposable
    {
        /// <summary>数据库默认文件名。</summary>
        public const string DefaultDatabaseFileName = "MSIF_Parts.db";

        /// <summary>数据库默认子文件夹名。</summary>
        public const string DefaultDatabaseFolderName = "Data";

        /// <summary>部品基本信息表名。</summary>
        public const string TableName = "部品基本信息";

        /// <summary>App.config 中指定数据库路径的键名。</summary>
        public const string DatabasePathSettingKey = "PartDatabasePath";

        private static readonly string[] ColumnNames = new string[]
        {
            "図番", "页码", "番号", "部品名", "部品図番", "枠区分", "員数",
            "基本寸法", "切断寸法", "部品コード", "校验状态", "备注", "来源表格文件", "来源PDF", "登录时间"
        };

        /// <summary>插入语句（列名与参数名共用 <see cref="ColumnNames"/>，避免两处不一致）。</summary>
        private static readonly string InsertSql = BuildInsertSql();


        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;

        /// <summary>
        /// 打开（不存在时创建）数据库并确保表结构存在。
        /// </summary>
        /// <param name="databasePath">数据库文件完整路径；为空时使用 <see cref="ResolveDatabasePath"/>。</param>
        public PartBasicInfoRepository(string databasePath)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? ResolveDatabasePath()
                : databasePath.Trim();

            string folder = Path.GetDirectoryName(_databasePath);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = _databasePath;
            builder.FailIfMissing = false;
            builder.ForeignKeys = true;

            _connection = new SQLiteConnection(builder.ToString());
            _connection.Open();

            EnsureSchema();
        }

        /// <summary>数据库文件的完整路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 解析数据库路径：App.config 的 PartDatabasePath（相对路径按程序目录）→ 默认 &lt;程序目录&gt;\Data\MSIF_Parts.db。
        /// </summary>
        public static string ResolveDatabasePath()
        {
            string configured = null;

            try
            {
                configured = ConfigurationManager.AppSettings[DatabasePathSettingKey];
            }
            catch (ConfigurationErrorsException)
            {
                configured = null;
            }

            if (!string.IsNullOrWhiteSpace(configured))
            {
                string value = configured.Trim();

                return Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDatabaseFolderName,
                DefaultDatabaseFileName);
        }

        /// <summary>
        /// 建表（已存在则跳过）并创建索引。
        /// </summary>
        private void EnsureSchema()
        {
            string createTable =
                "CREATE TABLE IF NOT EXISTS [" + TableName + "] (" +
                "[id] INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "[図番] TEXT NOT NULL, " +
                "[页码] INTEGER NOT NULL DEFAULT 0, " +
                "[番号] INTEGER NOT NULL, " +
                "[部品名] TEXT, " +
                "[部品図番] TEXT, " +
                "[枠区分] TEXT, " +
                "[員数] TEXT, " +
                "[基本寸法] TEXT, " +
                "[切断寸法] TEXT, " +
                "[部品コード] TEXT, " +
                "[校验状态] TEXT, " +
                "[备注] TEXT, " +
                "[来源表格文件] TEXT, " +
                "[来源PDF] TEXT, " +
                "[登录时间] TEXT NOT NULL" +
                ");";

            string createIndex =
                "CREATE UNIQUE INDEX IF NOT EXISTS [ux_" + TableName + "_図番_页码_番号] ON [" + TableName +
                "] ([図番], [页码], [番号]);";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                Execute(createTable, transaction);
                Execute(createIndex, transaction);
                transaction.Commit();
            }
        }

        private static string BuildInsertSql()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("INSERT OR REPLACE INTO [").Append(TableName).Append("] (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('[').Append(ColumnNames[index]).Append(']');
            }

            builder.Append(") VALUES (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('@').Append(ColumnNames[index]);
            }

            builder.Append(");");

            return builder.ToString();
        }

        private void Execute(string sql, SQLiteTransaction transaction)

        {
            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 登录一批部品基本信息（同一 図番 + 页码 + 番号 覆盖旧值），返回实际写入条数。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">要写入的部品基本信息。</param>
        public int Save(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            string loginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string key = drawingNo.Trim();
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 同一図番先清空，保证本次结果完整覆盖上次结果（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                {
                    foreach (string column in ColumnNames)
                    {
                        command.Parameters.Add(new SQLiteParameter("@" + column));
                    }

                    foreach (PartBasicInfo part in parts)
                    {
                        command.Parameters["@図番"].Value = key;
                        command.Parameters["@页码"].Value = part.Page;
                        command.Parameters["@番号"].Value = part.Number;
                        command.Parameters["@部品名"].Value = part.PartName;
                        command.Parameters["@部品図番"].Value = part.PartDrawingNo;
                        command.Parameters["@枠区分"].Value = part.FrameKind;
                        command.Parameters["@員数"].Value = part.Quantity;
                        command.Parameters["@基本寸法"].Value = part.BasicSize;
                        command.Parameters["@切断寸法"].Value = part.CutSize;
                        command.Parameters["@部品コード"].Value = part.PartCode;
                        command.Parameters["@校验状态"].Value = part.VerifyStatus;
                        command.Parameters["@备注"].Value = part.Remark;
                        command.Parameters["@来源表格文件"].Value = part.SourceFile;
                        command.Parameters["@来源PDF"].Value = part.PdfFile;
                        command.Parameters["@登录时间"].Value = loginTime;

                        written += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>
        /// 用给定内容整体替换某図番的部品明细（供画面手工编辑后保存）。
        /// 与 <see cref="Save"/> 的区别：本次未修改的行保留原「登录时间」，新增行才写入当前时间；
        /// <paramref name="parts"/> 为空表示清空该図番的全部明细。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">画面上当前的部品明细（全部行）。</param>
        /// <returns>实际写入的条数。</returns>
        public int ReplaceByDrawing(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            string key = drawingNo.Trim();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 先清空该図番，保证画面上的删除操作真正生效（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                if (parts != null && parts.Count > 0)
                {
                    using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                    {
                        foreach (string column in ColumnNames)
                        {
                            command.Parameters.Add(new SQLiteParameter("@" + column));
                        }

                        foreach (PartBasicInfo part in parts)
                        {
                            if (part == null)
                            {
                                continue;
                            }

                            string loginTime = string.IsNullOrWhiteSpace(part.LoginTime)
                                ? now
                                : part.LoginTime.Trim();

                            command.Parameters["@図番"].Value = key;
                            command.Parameters["@页码"].Value = part.Page;
                            command.Parameters["@番号"].Value = part.Number;
                            command.Parameters["@部品名"].Value = part.PartName;
                            command.Parameters["@部品図番"].Value = part.PartDrawingNo;
                            command.Parameters["@枠区分"].Value = part.FrameKind;
                            command.Parameters["@員数"].Value = part.Quantity;
                            command.Parameters["@基本寸法"].Value = part.BasicSize;
                            command.Parameters["@切断寸法"].Value = part.CutSize;
                            command.Parameters["@部品コード"].Value = part.PartCode;
                            command.Parameters["@校验状态"].Value = part.VerifyStatus;
                            command.Parameters["@备注"].Value = part.Remark;
                            command.Parameters["@来源表格文件"].Value = part.SourceFile;
                            command.Parameters["@来源PDF"].Value = part.PdfFile;
                            command.Parameters["@登录时间"].Value = loginTime;

                            written += command.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>统计指定図番已经登录的条数。</summary>
        public int Count(string drawingNo)
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "] WHERE [図番] = @図番;";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>统计表内全部条数。</summary>
        public int CountAll()
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 读取指定図番已经登录的全部部品基本信息（按页码、番号排序）。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        public IList<PartBasicInfo> Query(string drawingNo)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>();

            string sql =
                "SELECT [図番], [页码], [番号], [部品名], [部品図番], [枠区分], [員数], [基本寸法], " +
                "[切断寸法], [部品コード], [校验状态], [备注], [来源表格文件], [来源PDF], [登录时间] " +
                "FROM [" + TableName + "] WHERE [図番] = @図番 " +
                "ORDER BY [页码], [番号];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        PartBasicInfo part = new PartBasicInfo(
                            ReadText(reader, 0),
                            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            ReadText(reader, 3),
                            ReadText(reader, 4),
                            ReadText(reader, 5),
                            ReadText(reader, 6),
                            ReadText(reader, 7),
                            ReadText(reader, 8),
                            ReadText(reader, 9));

                        part.VerifyStatus = ReadText(reader, 10);
                        part.Remark = ReadText(reader, 11);
                        part.SourceFile = ReadText(reader, 12);
                        part.PdfFile = ReadText(reader, 13);
                        part.LoginTime = ReadText(reader, 14);

                        parts.Add(part);
                    }
                }
            }

            return parts;
        }

        /// <summary>从 PDF 路径解析図番（文件名去扩展名，与 AI 分析登录时一致）。</summary>
        public static string ResolveDrawingNo(string pdfFilePath)
        {
            return string.IsNullOrWhiteSpace(pdfFilePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(pdfFilePath.Trim());
        }

        /// <summary>读取可空文本列（NULL 视为空字符串）。</summary>
        private static string ReadText(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                }

                _connection.Dispose();
            }
        }
    }
}

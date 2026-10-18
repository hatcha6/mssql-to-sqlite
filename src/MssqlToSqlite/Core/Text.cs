using System;
using System.Data.SqlClient;
using System.IO;

namespace MssqlToSqlite.Core
{
    /// <summary>
    /// Every user-facing string, in Arabic (the Pointy/Daftar rule: no copy inline
    /// in UI code). The command-line usage text stays English — it is for us.
    /// </summary>
    public static class Text
    {
        public const string AppTitle = "تصدير بيانات SQL Server";
        public const string Brand = "دفتر";
        public const string AppSubtitle = "نقل قاعدة بيانات SQL Server 2005 وما بعده إلى ملف SQLite يمكن رفعه في دفتر";

        // Source card
        public const string SourceTitle = "المصدر";
        public const string SourceServer = "خادم";
        public const string SourceBackup = "نسخة ‎.bak";
        public const string SourceDataFile = "ملف ‎.mdf";
        public const string ServerLabel = "الخادم";
        public const string FindOnNetwork = "بحث في الشبكة";
        public const string Searching = "جارٍ البحث…";
        public const string AuthLabel = "طريقة الدخول";
        public const string AuthWindows = "حساب Windows";
        public const string AuthSql = "حساب SQL Server";
        public const string UserLabel = "اسم المستخدم";
        public const string PasswordLabel = "كلمة المرور";
        public const string FileLabel = "الملف";
        public const string Browse = "اختيار…";
        public const string Connect = "اتصال";
        public const string Connecting = "جارٍ الاتصال…";
        public const string DatabaseLabel = "قاعدة البيانات";
        public const string NotConnected = "غير متصل";
        public const string ConnectedChip = "متصل";
        public const string FailedChip = "فشل الاتصال";
        public const string FileModeHint = "يُستعاد الملف مؤقتًا على الخادم المحدد ثم يُحذف بعد التصدير. يجب أن يكون الخادم على هذا الجهاز. لا يُعدَّل الملف الأصلي.";
        public const string ServerModeHint = "أغلق برنامج البيع القديم قبل التصدير حتى لا تتغير البيانات أثناء النسخ.";
        public const string BackupFilter = "نسخة SQL Server الاحتياطية (*.bak)|*.bak|كل الملفات (*.*)|*.*";
        public const string DataFileFilter = "ملف بيانات SQL Server (*.mdf)|*.mdf|كل الملفات (*.*)|*.*";
        public const string SqliteFilter = "قاعدة بيانات SQLite (*.db;*.sqlite)|*.db;*.sqlite|كل الملفات (*.*)|*.*";
        public const string NoInstancesFound = "لم يُعثر على خوادم في الشبكة. اكتب اسم الخادم يدويًا.";
        public const string InstancesFound = "عُثر على {0} خادم.";

        // Tables card
        public const string TablesTitle = "الجداول";
        public const string SearchTables = "ابحث عن جدول…";
        public const string SelectAll = "تحديد الكل";
        public const string SelectNone = "إلغاء التحديد";
        public const string ColumnTable = "الجدول";
        public const string ColumnSchema = "المخطط";
        public const string ColumnRows = "الصفوف";
        public const string ColumnStatus = "الحالة";
        public const string TablesSummary = "محدد {0} من {1} · نحو {2} صف";
        public const string TablesEmpty = "اتصل بالخادم واختر قاعدة بيانات لعرض جداولها.";
        public const string StatusWaiting = "بالانتظار";
        public const string StatusCopying = "جارٍ النسخ… {0}";
        public const string StatusDone = "تم · {0}";
        public const string StatusFailed = "فشل";
        public const string LogTitle = "السجل";

        // Footer
        public const string OutputLabel = "ملف الحفظ";
        public const string TrimFixedText = "إزالة المسافات الزائدة من الحقول ثابتة الطول (char)";
        public const string Export = "تصدير";
        public const string Cancel = "إلغاء";
        public const string OpenFolder = "فتح المجلد";
        public const string Ready = "جاهز.";
        public const string Exporting = "جارٍ التصدير: {0} ({1} من {2})";
        public const string Cancelling = "جارٍ الإلغاء…";
        public const string Cancelled = "أُلغي التصدير. لم يُحفظ أي ملف.";
        public const string ExportDone = "اكتمل التصدير: {0} جدول، {1} صف، خلال {2}.";
        public const string ExportDoneWarnings = " ({0} تنبيه — راجع السجل)";
        public const string ExportFailed = "فشل التصدير: {0}";
        public const string ChooseDatabaseFirst = "اختر قاعدة بيانات أولًا.";
        public const string ChooseFileFirst = "اختر الملف أولًا.";
        public const string ChooseTablesFirst = "حدد جدولًا واحدًا على الأقل.";
        public const string ChooseOutputFirst = "اختر مكان حفظ الملف.";
        public const string EnterServerFirst = "اكتب اسم الخادم.";
        public const string ConfirmOverwrite = "الملف موجود مسبقًا وسيُستبدل عند اكتمال التصدير. متابعة؟";
        public const string ConfirmQuit = "التصدير جارٍ. هل تريد إلغاءه والخروج؟";

        // Engine log
        public const string ConnectedTo = "متصل بالخادم: {0}";
        public const string Restoring = "جارٍ استعادة النسخة الاحتياطية مؤقتًا…";
        public const string CopyingDataFile = "جارٍ نسخ ملف البيانات (لا يُعدَّل الأصل)…";
        public const string Attaching = "جارٍ ربط ملف البيانات مؤقتًا…";
        public const string Cleanup = "جارٍ إزالة قاعدة البيانات المؤقتة…";
        public const string CreatingIndexes = "جارٍ إنشاء الفهارس…";
        public const string TableHasNoColumns = "تخطي {0}: لا أعمدة قابلة للقراءة.";
        public const string UniqueIndexDowngraded = "الفهرس الفريد {1} على {0} أُنشئ كفهرس عادي لتكرار في القيم.";
        public const string DecimalsRounded = "{0}: {1} قيمة عشرية تتجاوز 15 رقمًا معنويًا خُزّنت مقرّبة (حد SQLite).";
        public const string TableCopied = "{0}: {1} صف";
        public const string FileNotFound = "الملف غير موجود.";

        /// <summary>A server error in words a shop technician can act on, with the original kept for us.</summary>
        public static string Describe(Exception error)
        {
            if (error is SqlException sql)
            {
                switch (sql.Number)
                {
                    case 18456:
                        return "فشل تسجيل الدخول: اسم المستخدم أو كلمة المرور غير صحيحة، أو أن الحساب لا يملك صلاحية. (" + sql.Message + ")";
                    case 2:
                    case 53:
                    case -1:
                    case 26:
                        return "تعذّر الوصول إلى الخادم. تأكد من اسم الخادم وأن خدمة SQL Server تعمل. (" + sql.Message + ")";
                    case 4060:
                        return "لا يمكن فتح قاعدة البيانات بهذا الحساب. (" + sql.Message + ")";
                    case 3169:
                    case 3241:
                        return "لا يستطيع هذا الخادم قراءة النسخة الاحتياطية: غالبًا هي من إصدار أحدث من الخادم، أو أن الملف تالف. (" + sql.Message + ")";
                    case 3201:
                    case 5120:
                    case 5133:
                        return "لا يستطيع SQL Server قراءة الملف من هذا المجلد. انسخ الملف إلى مجلد DATA الخاص بـ SQL Server وحاول مجددًا. (" + sql.Message + ")";
                    case 262:
                    case 3110:
                    case 1802:
                        return "الحساب لا يملك صلاحية استعادة أو ربط قواعد البيانات. استخدم حساب مدير على الخادم. (" + sql.Message + ")";
                }
                return sql.Message;
            }
            if (error is IOException || error is UnauthorizedAccessException)
                return "تعذّر الوصول إلى الملف: " + error.Message;
            return error.Message;
        }
    }
}

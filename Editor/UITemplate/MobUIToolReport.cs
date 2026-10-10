using System;
using System.Collections.Generic;

namespace Mob404.Common.UITemplate.Editor
{
    /// <summary>Kết quả một lượt Scan / Preview / Apply / Remove — hiển thị trong cửa sổ, bấm vào file để mở.</summary>
    public class MobUIToolReport
    {
        public class Entry
        {
            public string path;
            public MobUIToolAssetUtil.Result result;
            /// <summary>Số đối tượng (button/image/marker) đã đổi — hoặc sẽ đổi khi chạy thử.</summary>
            public int objects;
            public string skipReason;
            public string error;
            public readonly List<string> warnings = new List<string>();

            /// <summary>Đáng hiện trong danh sách: có đổi, bị bỏ qua, lỗi, hoặc có cảnh báo.</summary>
            public bool Notable => result != MobUIToolAssetUtil.Result.Unchanged || error != null || warnings.Count > 0;
        }

        public readonly string title;
        public readonly bool dryRun;
        public readonly DateTime time = DateTime.Now;
        public readonly List<Entry> entries = new List<Entry>();
        /// <summary>Cảnh báo không gắn với file nào (vd sprite trùng giữa hai loại).</summary>
        public readonly List<string> warnings = new List<string>();
        public string summary;
        public bool cancelled;

        public MobUIToolReport(string title, bool dryRun = false)
        {
            this.title = title;
            this.dryRun = dryRun;
        }

        public Entry Add(string path)
        {
            var entry = new Entry { path = path };
            entries.Add(entry);
            return entry;
        }

        public int Count(MobUIToolAssetUtil.Result result)
        {
            int n = 0;
            foreach (var e in entries)
                if (e.error == null && e.result == result) n++;
            return n;
        }

        public int Errors
        {
            get
            {
                int n = 0;
                foreach (var e in entries)
                    if (e.error != null) n++;
                return n;
            }
        }

        public int Objects
        {
            get
            {
                int n = 0;
                foreach (var e in entries) n += e.objects;
                return n;
            }
        }

        /// <summary>Một dòng tóm tắt cho thanh trạng thái và Console.</summary>
        public string Headline =>
            $"{title}{(dryRun ? " (xem trước)" : "")}: {summary} — " +
            $"{(dryRun ? "sẽ ghi" : "ghi")} {Count(MobUIToolAssetUtil.Result.Changed)} file, " +
            $"không đổi {Count(MobUIToolAssetUtil.Result.Unchanged)}, bỏ qua {Count(MobUIToolAssetUtil.Result.Skipped)}, lỗi {Errors}" +
            (cancelled ? " (đã huỷ giữa chừng)" : "") + $" — {time:HH:mm:ss}";
    }
}

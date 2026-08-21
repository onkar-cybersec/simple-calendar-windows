using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SimpleCalendar
{
    internal static class TileService
    {
        public static void UpdateAndSchedule()
        {
            try
            {
                TileUpdater updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(false);
                updater.Clear();

                IReadOnlyList<ScheduledTileNotification> pending = updater.GetScheduledTileNotifications();
                foreach (ScheduledTileNotification item in pending)
                    updater.RemoveFromSchedule(item);

                DateTime today = DateTime.Today;
                TileNotification current = new TileNotification(Build(today));
                current.ExpirationTime = new DateTimeOffset(today.AddDays(1).AddSeconds(-1));
                updater.Update(current);

                // Schedule a full year locally. No account, network, service, or
                // background process is needed for the tile to roll over at midnight.
                for (int offset = 1; offset <= 370; offset++)
                {
                    DateTime date = today.AddDays(offset);
                    DateTime delivery = date.AddSeconds(2);
                    ScheduledTileNotification scheduled = new ScheduledTileNotification(
                        Build(date), new DateTimeOffset(delivery));
                    scheduled.ExpirationTime = new DateTimeOffset(date.AddDays(1).AddSeconds(-1));
                    updater.AddToSchedule(scheduled);
                }
            }
            catch
            {
                // The portable EXE has no package identity. It still works normally;
                // live-tile APIs become active after installing the signed MSIX.
            }
        }

        private static XmlDocument Build(DateTime date)
        {
            string number = date.Day.ToString(CultureInfo.InvariantCulture);
            string xml =
                "<tile>" +
                "<visual branding='none'>" +
                "<binding template='TileSmall' branding='none' hint-textStacking='center'>" +
                "<text hint-style='subtitle' hint-align='center'>" + number + "</text>" +
                "</binding>" +
                "<binding template='TileMedium' branding='none' hint-textStacking='center'>" +
                "<text hint-style='header' hint-align='center'>" + number + "</text>" +
                "</binding>" +
                "<binding template='TileWide' branding='none' hint-textStacking='center'>" +
                "<text hint-style='header' hint-align='center'>" + number + "</text>" +
                "</binding>" +
                "<binding template='TileLarge' branding='none' hint-textStacking='center'>" +
                "<text hint-style='header' hint-align='center'>" + number + "</text>" +
                "</binding>" +
                "</visual>" +
                "</tile>";
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            return document;
        }
    }
}

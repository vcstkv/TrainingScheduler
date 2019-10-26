﻿using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TrainingScheduler
{
    public class DatabaseManager
    {
        private SQLiteConnection db;
        private DropboxManager dropbox;
        private string name;
        private bool selfLocked;
        private string lockFileName = ".lock";
        public DatabaseManager(string name)
        {
            dropbox = new DropboxManager();
            this.name = name;
            lockFileName = name + lockFileName;
            selfLocked = false;
        }

        public async Task ExecuteNonQueryAsync(SQLiteCommand command)
        {
            Debug.WriteLine("ExecuteNonQueryAsync");
            await FetchDb();
            command.Connection = db;
            command.ExecuteNonQuery();
            Debug.WriteLine("ExecuteNonQueryAsync query executed");
            command.Connection = null;
            await CommitDb();
        }

        public async Task<object> ExecuteScalarAsync(SQLiteCommand command)
        {
            Debug.WriteLine("ExecuteScalarAsync");
            await FetchDb();
            command.Connection = db;
            object ret = command.ExecuteScalar();
            Debug.WriteLine("ExecuteScalarAsync executed");
            command.Connection = null;
            await CommitDb();
            return ret;
        }

        public async Task<List<object[]>> ExecuteReaderAsync(SQLiteCommand command)
        {
            Debug.WriteLine("ExecuteReaderAsync");
            await FetchDb();
            List<object[]> ret = new List<object[]>();
            command.Connection = db;
            SQLiteDataReader reader = command.ExecuteReader();
            Debug.WriteLine("ExecuteReaderAsync executed");
            if (reader.HasRows)
            {
                while (reader.Read())
                {
                    object[] row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    ret.Add(row);
                }
            }
            reader.Close();
            Debug.WriteLine("ExecuteReaderAsync reader closed");
            command.Connection = null;
            await CommitDb();
            return ret;
        }

        private async Task<bool> FetchDb()
        {
            Debug.WriteLine("FetchDb");
            if (selfLocked)
            {
                Debug.WriteLine("FetchDb selfLocked");
                return true;
            }

            bool locked = true;

            for (int i = 0; i < 20; i++)
            {
                locked = await IsLocked();
                if (!locked)
                {
                    break;
                }
                await Task.Delay(1000);
            }

            Debug.WriteLine("FetchDb unlocked, trying to lock");

            await Lock();
            selfLocked = true;

            Debug.WriteLine("FetchDb locked");

            if (await dropbox.CheckFileExists(name))
            {
                Debug.WriteLine("FetchDb db exists on dropbox");
                await dropbox.Download(name);
                Debug.WriteLine("FetchDb downloaded db from dropbox");
            }
            else
            {
                Debug.WriteLine("FetchDb db not exists on dropbox");
                SQLiteConnection.CreateFile(name);
                Debug.WriteLine("FetchDb created new db");
            }

            db = new SQLiteConnection("Data Source=" + name + ";Version=3;");
            db.Open();

            Debug.WriteLine("FetchDb open db connection");

            return true;
        }

        private async Task<bool> CommitDb()
        {
            Debug.WriteLine("CommitDb");
            if (!selfLocked)
            {
                Debug.WriteLine("CommitDb not selflocked");
                return false;
            }
            SQLiteConnection.ClearPool(db);
            db.Close();
            Debug.WriteLine("CommitDb close db");
            await dropbox.Upload(name);
            Debug.WriteLine("CommitDb db uploaded to dropbox");
            await Unlock();
            Debug.WriteLine("CommitDb dropbox unlocked");
            selfLocked = false;
            return true;
        }

        private async Task Lock()
        {
            await dropbox.Upload(lockFileName, "");
        }

        private async Task Unlock()
        {
            await dropbox.Delete(lockFileName);
        }

        private async Task<bool> IsLocked()
        {
            return await dropbox.CheckFileExists(lockFileName);
        }
    }
}

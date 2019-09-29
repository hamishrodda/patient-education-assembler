﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using Word = Microsoft.Office.Interop.Word;
using QRCoder;
using System.Data.OleDb;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Data.SqlClient;
using System.Data;

namespace Patient_Education_Assembler
{
    public abstract class PatientEducationObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public PatientEducationProvider ParentProvider { get; private set; }

        private void OnPropertyChanged<T>([CallerMemberName]string caller = null)
        {
            // make sure only to call this if the value actually changes

            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(caller));
            }
        }

        public static String baseFileName()
        {
            return EducationDatabase.Self().CachePath + "/";
        }

        protected String rtfFileName()
        {
            return baseFileName() + ThisGUID.ToString() + ".rtf";
        }

        public static String imagesPath()
        {
            return baseFileName() + "images/";
        }

        protected String imagesFileName()
        {
            return imagesPath() + ThisGUID.ToString();
        }

        public static String cachePath()
        {
            return baseFileName() + "cache/";
        }

        protected virtual String cacheFileName()
        {
            return cachePath() + ThisGUID.ToString() + "." + cacheExtension();
        }

        public bool isCached()
        {
            return File.Exists(cacheFileName());
        }

        public DateTime cacheDate()
        {
            return File.GetLastWriteTime(cacheFileName());
        }
        
        internal static void cleanupWord()
        {
            if (wordApp != null)
            {
                wordApp.Quit();
                wordApp = null;
            }
        }

        public abstract string cacheExtension();

        private static bool invisible = true, closeDocs = true;

        // Database fields
        public int AreaID;
        public int CategoryID;
        public int Language_ID;
        public int Doc_LangID;
        public int Doc_ID;

        public Uri URL { get; set; }

        public string FileName;
        public String Title { get { return Title; } set { Title = value.Replace("(", Char.ConvertFromUtf32(0x2768)).Replace(")", Char.ConvertFromUtf32(0x2768)); } }
        public bool Enabled { get; set; }

        public string CacheDate {
            get {
                if (cacheDate() > new DateTime(2016, 1, 1))
                    return cacheDate().ToShortDateString();
                else
                    return "Not cached";
            }
        }

        public bool FromDatabase { get; set; }

        public String Status {
            get {
                switch (LoadStatus)
                {
                    
                    case LoadStatusEnum.DatabaseEntry:
                        return "Database Entry";
                    case LoadStatusEnum.DatabaseAndIndexMatched:
                        return "DB + Index Entry";
                    case LoadStatusEnum.Waiting:
                        return "Waiting to download";
                    case LoadStatusEnum.Retrieving:
                        return "Downloading";
                    case LoadStatusEnum.Downloaded:
                        return "Downloaded";
                    case LoadStatusEnum.AccessError:
                        return "Access Error";
                    case LoadStatusEnum.Parsing:
                        return "Parsing";
                    case LoadStatusEnum.ParseError:
                        return "Parse Error";
                    case LoadStatusEnum.LoadedSucessfully:
                        return "Complete";
                    case LoadStatusEnum.NewFromWebIndex:
                        return "New Document";
                    case LoadStatusEnum.DocumentIgnored:
                        return "Document Ignored";
                    case LoadStatusEnum.RemovedByContentProvider:
                        return "Removed by Content Provider";
                    default:
                        return "Undefined";
                }
            }
        }

        public enum LoadStatusEnum
        {
            DatabaseEntry,
            NewFromWebIndex,
            DatabaseAndIndexMatched,
            Waiting,
            Retrieving,
            Downloaded,
            AccessError,
            Parsing,
            ParseError,
            LoadedSucessfully,
            DocumentIgnored,
            RemovedByContentProvider
        }
        private LoadStatusEnum currentLoadStatus;
        public LoadStatusEnum LoadStatus { get { return currentLoadStatus; } protected set { currentLoadStatus = value; OnPropertyChanged<String>(Status); } }

        public abstract void retrieveSourceDocument();
        public abstract void parseDocument();

        public String ReviewStatus { get; }

        public Guid ThisGUID { get; set; }

        public Dictionary<int, string> Synonyms { get; set; }
        public void AddSynonym(string synonym)
        {
            if (!Synonyms.ContainsValue(synonym))
                Synonyms.Add(EducationDatabase.Self().GetNewSynonymID(), synonym);
        }

        static protected Word.Application wordApp;
        protected Word.Document thisDoc;
        protected Word.Range currentRange;
        protected bool wantNewLine;
        protected bool wantNewParagraph;

        public bool DocumentParsed { get; set; }

        // New document constructor for not previously accessed URLs
        public PatientEducationObject(PatientEducationProvider provider, Uri url)
        {
            ParentProvider = provider;
            FromDatabase = false;
            DocumentParsed = false;
            LoadStatus = LoadStatusEnum.NewFromWebIndex;

            // Setup defaults and IDs for new documents
            AreaID = 1;
            Language_ID = 1;
            CategoryID = 1;
            Doc_LangID = 1; // English (default) TODO support other languages
            Doc_ID = -1;

            URL = url;

            ThisGUID = Guid.NewGuid();
            FileName = ThisGUID + ".rtf";

            Synonyms = new Dictionary<int, string>();
            createWordApp();
        }

        // New document constructor for index URLs
        public PatientEducationObject(PatientEducationProvider provider, Uri url, Guid guid)
        {
            ParentProvider = provider;
            FromDatabase = true;
            DocumentParsed = false;
            LoadStatus = LoadStatusEnum.NewFromWebIndex;

            // Setup defaults and IDs for new documents
            AreaID = 1;
            Language_ID = 1;
            CategoryID = 1;
            Doc_LangID = 1; // English (default) TODO support other languages
            Doc_ID = -1;

            URL = url;

            if (guid == Guid.Empty)
                guid = Guid.NewGuid();
            else
                ThisGUID = guid;
            
            createWordApp();
        }

        // Database load document constructor
        public PatientEducationObject(PatientEducationProvider provider, OleDbDataReader reader)
        {
            ParentProvider = provider;
            FromDatabase = true;
            DocumentParsed = false;
            LoadStatus = LoadStatusEnum.DatabaseEntry;

            // Setup defaults and IDs for loaded documents
            AreaID = 1;
            Language_ID = 1;
            CategoryID = 1;
            Doc_LangID = (int)reader.GetDouble((int)EducationDatabase.MetadataColumns.Doc_Lang_Id);
            Doc_ID = (int)reader.GetDouble((int)EducationDatabase.MetadataColumns.Doc_ID);
            Title = reader.GetString((int)EducationDatabase.MetadataColumns.Document_Name);
            Enabled = reader.GetBoolean((int)EducationDatabase.MetadataColumns.Enabled);

            URL = new Uri(reader.GetString((int)EducationDatabase.MetadataColumns.URL));
            
            ThisGUID = new Guid(reader.GetString((int)EducationDatabase.MetadataColumns.GUID));
            FileName = ThisGUID + ".rtf";

            Synonyms = new Dictionary<int, string>();

            createWordApp();
        }

        public static void createWordApp()
        {
            if (wordApp == null)
            {
                if ((bool)MainWindow.thisWindow.ShowWord.IsChecked)
                    invisible = false;

                wordApp = new Word.Application();
                wordApp.Visible = !invisible;
            }
        }

        public void CreateDocument()
        {
            thisDoc = wordApp.Documents.Add();
            currentRange = thisDoc.Range();
            wantNewLine = false;
            wantNewParagraph = false;

            boldRanges = new List<Tuple<int, int>>();
            highlightRanges = new List<Tuple<int, int>>();
            emphasisRanges = new List<Tuple<int, int>>();
            underlineRanges = new List<Tuple<int, int>>();
        }

        internal void LoadSynonym(int synonymID, string synonym)
        {
            if (!Synonyms.ContainsKey(synonymID))
                Synonyms.Add(synonymID, synonym);
        }

        protected static bool skipUntilNextH2 = false;
        protected static bool inHighlight = false;
        protected static int latestBlockStart = 0;

        protected List<Tuple<int, int>> boldRanges, highlightRanges, emphasisRanges, underlineRanges;

        private static string ShowHexValue(string s)
        {
            string retval = null;
            foreach (var ch in s)
            {
                byte[] bytes = BitConverter.GetBytes(ch);
                retval += String.Format("{0:X2} {1:X2} ", bytes[1], bytes[0]);
            }
            return retval;
        }

        public virtual void FinishDocument(string fontFamily = "Calibri")
        {
            // apply bold ranges
            if (boldRanges.Count > 1)
                foreach (Tuple<int, int> boldRange in boldRanges)
                {
                    currentRange.SetRange(boldRange.Item1, boldRange.Item2);
                    currentRange.Font.Bold = 1;
                    //Console.WriteLine("Bolding range: ({0}, {1}) => {2}", currentRange.Start, currentRange.End, currentRange.Text);
                }
            boldRanges = null;

            if (highlightRanges.Count > 1)
                foreach (Tuple<int, int> highlightRange in highlightRanges)
                {
                    currentRange.SetRange(highlightRange.Item1, highlightRange.Item2);
                    currentRange.Font.Color = Word.WdColor.wdColorRed;
                }
            highlightRanges = null;

            if (emphasisRanges.Count > 1)
                foreach (Tuple<int, int> emphasisRange in emphasisRanges)
                {
                    currentRange.SetRange(emphasisRange.Item1, emphasisRange.Item2);
                    currentRange.Font.Italic = 1;
                }
            emphasisRanges = null;

            if (underlineRanges.Count > 1)
                foreach (Tuple<int, int> underlineRange in underlineRanges)
                {
                    currentRange.SetRange(underlineRange.Item1, underlineRange.Item2);
                    currentRange.Font.Underline = Word.WdUnderline.wdUnderlineSingle;
                }
            underlineRanges = null;

            currentRange = thisDoc.Range();
            currentRange.Font.Name = fontFamily;

            thisDoc.SaveAs2(rtfFileName(), Word.WdSaveFormat.wdFormatRTF);
            if (closeDocs)
                thisDoc.Close();
            thisDoc = null;

            DocumentParsed = true;
        }

        protected void AddHeading(string text, string style = "")
        {
            NewParagraph(style.Length > 0 ? style : "Heading 3");
            currentRange.InsertAfter(text);

            wantNewLine = false;
            wantNewParagraph = true;

            //Console.WriteLine("New Heading Paragraph: {0}", text);
        }

        protected void SetStyle(string style)
        {
            object wordStyle = style;
            currentRange.set_Style(ref wordStyle);
            //Console.WriteLine("Style: {0}", style);
        }
        
        protected void NewParagraph(string style = "")
        {
            currentRange.InsertParagraphAfter();
            currentRange = thisDoc.Paragraphs.Last.Range;
            latestBlockStart = currentRange.Start;
            
            SetStyle(style.Length > 0 ? style : "Normal");

            wantNewLine = false;
            wantNewParagraph = false;

            //Console.WriteLine("New Paragraph");
        }

        protected void TrimAndAddText(string text)
        {
            int startLen = text.Length;
            text = text.TrimStart();
            if (text.Length == 0)
                return;

            if (text.Length < startLen && !wantNewLine && !wantNewParagraph)
                text = ' ' + text;

            text = text.TrimEnd();

            if (text.Length < startLen)
                text += ' ';

            AddText(text);
        }

        protected void AddText(string text)
        {
            if (wantNewParagraph)
            {
                //Console.WriteLine("Wanted new paragraph");
                NewParagraph();
            }
            else
            if (wantNewLine)
            {
                //Console.WriteLine("Wanted new line");
                currentRange.InsertAfter("\n");
                latestBlockStart = currentRange.End;
                wantNewLine = false;
            }

            /*if (currentRange.Text == "\n")
                currentRange.Text = text;
            else*/
            currentRange.InsertAfter(text);

            //Console.WriteLine("Content text: '{0}'", text);
        }


        protected void StartBulletList()
        {
            NewParagraph();
            currentRange.ListFormat.ApplyBulletDefault();
            currentRange.Start = currentRange.End;
            //Console.WriteLine("Start Bullet List");
        }

        protected void StartOrderedList()
        {
            NewParagraph();
            currentRange.ListFormat.ApplyNumberDefault();
            currentRange.Start = currentRange.End;
            //Console.WriteLine("Start Numbered List");
        }

        protected void EndList()
        {
            //Console.WriteLine("End Bullet List");
            wantNewParagraph = true;
        }

        protected void AddWebImage(string relUrl, bool rightAlign = false)
        {
            if (wantNewParagraph)
                NewParagraph();

            using (WebClient client = new WebClient())
            {
                Uri imageUri = new Uri(URL, relUrl);
                string path = imageUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
                string fileName = path.Substring(path.LastIndexOf('/') + 1);

                string resultFile = imagesPath() + fileName;

                bool fileAvailable = File.Exists(resultFile);
                if (!fileAvailable)
                {
                    try
                    {
                        client.DownloadFile(imageUri, resultFile);
                        fileAvailable = true;
                    }
                    catch (WebException e)
                    {
                        Console.WriteLine("Download issue: {0}", e.ToString());
                    }
                }

                if (fileAvailable)
                {
                    if (rightAlign)
                    {
                        Word.Shape s = thisDoc.Shapes.AddPicture(resultFile, false, true, currentRange);
                        s.Left = (float)Word.WdShapePosition.wdShapeRight;
                    }
                    else
                    {
                        Word.InlineShape s = thisDoc.InlineShapes.AddPicture(resultFile, false, true, currentRange);
                    }

                    currentRange = thisDoc.Paragraphs.Last.Range;
                }
            }

            //Console.WriteLine("Image: {0}", relUrl);
        }

        protected void InsertQRCode(Uri url)
        {
            string qrPath = cacheFileName() + ".qr.png";
            if (!File.Exists(qrPath))
            {
                // Generate matching QR code for this file, as we have not yet done so already
                QRCodeGenerator qrGenerator = new QRCodeGenerator();
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url.AbsoluteUri, QRCodeGenerator.ECCLevel.Q);
                BitmapByteQRCode qrCode = new BitmapByteQRCode(qrCodeData);
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                using (System.Drawing.Image image = System.Drawing.Image.FromStream(new MemoryStream(qrCodeImage)))
                {
                    image.Save(qrPath, System.Drawing.Imaging.ImageFormat.Png);  // Or Png
                }
            }

            // Insert QR code into the document
            Word.InlineShape wordQR = thisDoc.InlineShapes.AddPicture(qrPath, false, true, currentRange);
            wordQR.Width = 100;
            wordQR.Height = 100;
        }

        protected void mergeWith(PatientEducationObject input)
        {
            // Only want to copy in new data at the index level
            URL = input.URL;
            Title = input.Title;
            foreach (KeyValuePair<int, string> pair in input.Synonyms)
                if (!Synonyms.ContainsKey(pair.Key))
                    Synonyms.Append(pair);

            // This object now has a database entry and an index entry
            LoadStatus = LoadStatusEnum.DatabaseAndIndexMatched;
        }

        public virtual void SaveToDatabase(OleDbConnection conn)
        {
            // Always insert into metadata
            OleDbCommand metaData = conn.CreateCommand();
            OleDbCommand docCat = conn.CreateCommand();
            OleDbCommand docTrans = conn.CreateCommand();
            {

                if (FromDatabase)
                {
                    metaData.CommandText = "UPDATE DocumentAssemblerMetadata SET " +
                        "FileName = @fn, Doc_Lang_ID = @doclang, Document_Name = @title, Language_ID = @lang, " +
                        "GenderID = @gender, AgeID = @age, URL = @url, Enabled = @enabled, " +
                        "ContentProvider = @provider, Bundle = @bundle, [GUID] = @thisguid " +
                        "WHERE Doc_ID = @doc";
                }
                else
                {
                    if (Doc_ID == -1)
                        Doc_ID = EducationDatabase.Self().GetNewDocID();

                    metaData.CommandText = "INSERT INTO DocumentAssemblerMetadata (" +
                        "FileName, Doc_Lang_Id, Document_Name, Language_ID, " +
                        "GenderID, AgeID, URL, Enabled, ContentProvider, Bundle, [GUID], Doc_ID" +
                        ") " +
                        "VALUES (@fn, @doclang, @title, @lang, " +
                        "@gender, @age, @url, @enabled, @provider, @bundle, @thisguid, @doc" +
                        ")";
                }

                if (FromDatabase && Enabled)
                {
                    // It will be in the main tables - UPDATE.  DocCat will already be correct.
                    docTrans.CommandText = "UPDATE DocumentTranslations SET " +
                        "FileName = @fn, Doc_Lang_ID = @doclang, Document_Name = @title, Language_ID = @lang, " +
                        "GenderID = @gender, AgeID = @age, URL = @url " +
                        "WHERE Doc_ID = @doc";
                }
                else
                {
                    docCat.CommandText = "INSERT INTO DocCat (Doc_ID, CategoryID) " +
                        "VALUES (@doc, @cat)";

                    docTrans.CommandText = "INSERT INTO DocumentTranslations (" +
                        "FileName, Doc_Lang_Id, Document_Name, Language_ID, " +
                        "GenderID, AgeID, URL, Doc_ID" +
                        ") " +
                        "VALUES (@fn, @doclang, @title, @lang, " +
                        "@gender, @age, @url, @doc" +
                        ")";
                }

                metaData.Parameters.Add("@fn", OleDbType.VarChar, 255).Value = FileName;
                metaData.Parameters.Add("@doclang", OleDbType.Double).Value = (double)Doc_LangID;
                metaData.Parameters.Add("@title", OleDbType.VarChar, 255).Value = Title;
                metaData.Parameters.Add("@lang", OleDbType.Double).Value = (double)Language_ID;
                metaData.Parameters.Add("@gender", OleDbType.BigInt).Value = (long)-1;
                metaData.Parameters.Add("@age", OleDbType.BigInt).Value = (long)-1;
                metaData.Parameters.Add("@url", OleDbType.VarChar, 255).Value = URL.ToString();
                metaData.Parameters.Add("@enabled", OleDbType.Boolean).Value = Enabled;
                metaData.Parameters.Add("@provider", OleDbType.VarChar, 255).Value = ParentProvider.contentProviderName;
                metaData.Parameters.Add("@bundle", OleDbType.VarChar, 255).Value = ParentProvider.contentBundleName;
                metaData.Parameters.Add("@thisguid", OleDbType.VarChar, 255).Value = ThisGUID.ToString();
                metaData.Parameters.Add("@doc", OleDbType.Double).Value = (double)Doc_ID;

                metaData.ExecuteNonQuery();

                if (docCat.CommandText.Length > 0)
                {
                    docCat.Parameters.Add("@doc", OleDbType.Double).Value = (double)Doc_ID;
                    docCat.Parameters.Add("@cat", OleDbType.BigInt).Value = (long)1;

                    docCat.ExecuteNonQuery();
                }

                if (docTrans.CommandText.Length > 0)
                {
                    docTrans.Parameters.Add("@fn", OleDbType.VarChar, 255).Value = FileName;
                    docTrans.Parameters.Add("@doclang", OleDbType.Double).Value = (double)Doc_LangID;
                    docTrans.Parameters.Add("@title", OleDbType.VarChar, 255).Value = Title;
                    docTrans.Parameters.Add("@lang", OleDbType.Double).Value = (double)Language_ID;
                    docTrans.Parameters.Add("@gender", OleDbType.BigInt).Value = (long)-1;
                    docTrans.Parameters.Add("@age", OleDbType.BigInt).Value = (long)-1;
                    docTrans.Parameters.Add("@url", OleDbType.VarChar, 255).Value = URL.ToString();
                    docTrans.Parameters.Add("@doc", OleDbType.Double).Value = (double)Doc_ID;

                    docTrans.ExecuteNonQuery();
                }
            }

            /*docCatTableAdapter.Insert(obj.Doc_ID, obj.CategoryID);
            documentTranslationsTableAdapter.Insert(obj.FileName, obj.Doc_ID, obj.Language_ID, obj.Title, obj.Language_ID, -1, -1, obj.URL.AbsoluteUri);
            foreach (string synonym in obj.Synonyms.Values)
            {
                synonymTableAdapter.Insert(obj.Doc_ID, synonym);
            }*/
        }
    }
}

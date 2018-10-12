﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Data.OleDb;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Patient_Education_Assembler
{
    public class HTMLContentProvider : INotifyPropertyChanged
    {
        static Dictionary<Uri, HTMLDocument> educationObjects;
        static ObservableCollection<HTMLDocument> educationCollection;

        XElement providerSpecification;
        String contentProviderName;
        Uri contentProviderUrl;
        Uri bundleUrl;
        Uri sourceXML;
        LoadDepth currentLoadDepth;
        int loadCount;

        public enum LoadDepth { Full, OneDocument, IndexOnly, TopLevel };

        //Information in this education material was downloaded by %ORGANISATION% from %PROVIDER% on %CACHEDATE%, and may have been modified by your doctor.For further information, and the latest version, go to their website - either scan the QR code, or copy the following address into your web browser:

        public HTMLContentProvider(Uri sourceXMLFile)
        {
            if (educationObjects == null) 
                educationObjects = new Dictionary<Uri, HTMLDocument>();

            if (educationCollection == null)
                educationCollection = new ObservableCollection<HTMLDocument>();

            sourceXML = sourceXMLFile;
        }

        public ObservableCollection<HTMLDocument> getEducationCollection()
        {
            return educationCollection;
        }

        public void loadDocument(OleDbDataReader reader)
        {
            string bundleName = reader.GetString((int)EducationDatabase.MetadataColumns.Bundle);
            foreach (XElement bundleSpec in providerSpecification.DescendantNodes())
            {
                if (bundleSpec.Name == "Bundle" && bundleSpec.Attribute("name") != null && bundleSpec.Attribute("name").Value == bundleName)
                {
                    HTMLDocument loadDoc = new HTMLDocument(bundleSpec, reader);
                    educationObjects.Add(loadDoc.URL, loadDoc);
                    educationCollection.Add(loadDoc);
                }
            }
        }

        public void loadSpecifications(LoadDepth depth = LoadDepth.Full)
        {

            currentLoadDepth = depth;
            loadCount = 0;

            XDocument specDoc = XDocument.Load(sourceXML.LocalPath);

            //try
            {
                XElement top = specDoc.Element("CustomPatientEducation");
                providerSpecification = top.Element("ContentProvider");
                contentProviderName = providerSpecification.Attribute("name").Value.ToString();
                MainWindow.thisWindow.CurrentContentProviderName.Text = contentProviderName;

                string tempUri = providerSpecification.Attribute("url").Value.ToString();
                contentProviderUrl = new Uri(tempUri);

                if (currentLoadDepth != LoadDepth.TopLevel)
                {
                    XElement e = providerSpecification.Element("Bundle");
                    if (!e.IsEmpty)
                    {
                        bundleUrl = new Uri(contentProviderUrl, e.Attribute("url").Value.ToString());
                        ParseBundle(e);
                    }
                }
            } /*catch (Exception e)
            {
                MessageBox.Show("Unhandled Exception: " + e.ToString(), "Patient Education Assembler");
            }*/
        }

        public void parseSpecifications(XElement e)
        {
            bundleUrl = new Uri(contentProviderUrl, e.Attribute("url").Value.ToString());

            foreach (XElement node in e.DescendantNodes())
                if (node.ToString() == "Document")
                    ParseBundle(node);
        }

        // Handles single index pages which explode to more index pages (xpath attribute)
        private void ParseBundle(XElement node)
        {
            HTMLBase indexDocument = new HTMLBase(new Uri(bundleUrl, node.Attribute("url").Value.ToString()));
            indexDocument.LoadWeb();
            
            if (node.Attribute("subIndexXPath") != null)
            {
                foreach (HtmlNode indexLink in indexDocument.doc.DocumentNode.SelectNodes(node.Attribute("subIndexXPath").Value.ToString()))
                {
                    HTMLBase subIndex = new HTMLBase(new Uri(bundleUrl, indexLink.GetAttributeValue("href", "")));
                    subIndex.LoadWeb();
                    ParseIndex(node, subIndex.doc);
                }
            } else
            {
                ParseIndex(node, indexDocument.doc);
            }
        }

        /// <summary>
        /// Iterates available links on the loaded index page
        /// </summary>
        /// <param name="node">The content loading configuration, at the level of node "Document"</param>
        /// <param name="doc">The</param>
        private void ParseIndex(XElement node, HtmlDocument doc)
        {
            foreach (XElement specDoc in node.Elements("Document"))
            {
                foreach (HtmlNode document in doc.DocumentNode.SelectNodes(specDoc.Attribute("urlXPath").Value.ToString()))
                    LoadDocument(specDoc, document);
            }
        }

        /// <summary>
        /// Creates new @ref HTMLDocument objects in the basis of the link URLs provided
        /// </summary>
        /// <param name="node">The content loading configuration, at the level of node "Document"</param>
        /// <param name="documentLink">The individual A-node which contains the href link</param>
        private void LoadDocument(XElement node, HtmlNode documentLink)
        {
            Uri link = new Uri(contentProviderUrl, documentLink.GetAttributeValue("href", ""));
            string title = System.Net.WebUtility.HtmlDecode(documentLink.InnerText.Trim());

            // Handle index level synonyms
            string synonym = "";
            if (node.Attribute("synonymRegExp") != null)
            {
                Regex exp = new Regex(node.Attribute("synonymRegExp").Value.ToString());
                Match m = exp.Match(title);
                if (m.Success)
                {
                    synonym = m.Groups["title"].Value.Trim();
                    title = m.Groups["synonym"].Value.Trim();
                }
            }

            //Console.WriteLine("One listing found, href {0} title {1} synonym {2}", link, title, synonym);

            //if (!title.Contains("Asthma"))
            //continue;

            //if (count < 10)
            {
                //++count;
                HTMLDocument thisPage;
                if (!educationObjects.ContainsKey(link))
                {
                    thisPage = new HTMLDocument(node, link);

                    if (currentLoadDepth == LoadDepth.OneDocument)
                    {
                        if (loadCount == 0)
                        {
                            loadCount++;
                            thisPage.loadDocument();
                        }

                    } else if (currentLoadDepth == LoadDepth.Full)
                    {
                        loadCount++;
                        thisPage.loadDocument();
                    }

                    if (thisPage.LoadSucceeded)
                        educationObjects.Add(link, thisPage);
                    else
                        educationObjects.Add(link, null);

                    educationCollection.Add(thisPage);

                } else
                {
                    thisPage = educationObjects[link];

                    if (!thisPage.DocumentParsed && currentLoadDepth == LoadDepth.Full)
                        thisPage.loadDocument();
                }

                if (synonym.Count() > 0)
                    thisPage.AddSynonym(synonym);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propName)
        {
            if (this.PropertyChanged != null)
                this.PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }
}
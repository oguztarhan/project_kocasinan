/*
===================================================================
Unity Assets by Yel Scrypt Fire Studio: https://yelsfstudio.com/game-assets/
===================================================================

Online Docs (Latest): https://yelsfstudio.com/game-assets/
Offline Docs: You have a PDF file in the package folder.

=======
SUPPORT
=======

First of all, read the docs. If it didn’t help, get the support.

Web: https://yelsfstudio.com/contact/
Email: support@yelsfstudio.com

If you find a bug or you can’t use the asset as you need,
please first send email to support@yelsfstudio.com
before leaving a review to the asset store.

We are here to help you and to improve our products for the best.
*/

using System;

using UnityEngine;

namespace LPRVFP
{
    public class LPRVFPPublisherReadme : ScriptableObject
    {
        public Texture2D icon;
        public string title;
        public Section[] sections;

        [Serializable]
        public class Section
        {
            public string heading, text, linkText, url;
        }
    }
}
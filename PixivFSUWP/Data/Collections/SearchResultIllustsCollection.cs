﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;
using System.Web;
using Windows.Data.Json;

namespace PixivFSUWP.Data.Collections
{
    public class SearchResultIllustsCollection : ObservableCollection<ViewModels.WaterfallItemViewModel>, ISupportIncrementalLoading
    {
        readonly string word;
        readonly string searchTarget;
        readonly string sort;
        readonly string duration;
        string nexturl = "begin";
        bool _busy = false;
        bool _emergencyStop = false;
        EventWaitHandle pause = new ManualResetEvent(true);

        public SearchResultIllustsCollection(string Word, string SearchTarget = "partial_match_for_tags",
            string Sort = "date_desc", string Duration = null)
        {
            word = Word;
            searchTarget = SearchTarget;
            sort = Sort;
            duration = null;
        }

        public bool HasMoreItems
        {
            get => nexturl != "";
        }

        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            if (_busy)
                throw new InvalidOperationException("Only one operation in flight at a time");
            _busy = true;
            return AsyncInfo.Run((c) => LoadMoreItemsAsync(c, count));
        }

        public void StopLoading()
        {
            _emergencyStop = true;
            if (_busy)
            {
                ResumeLoading();
            }
            else
            {
                Clear();
                GC.Collect();
            }
        }

        public void PauseLoading()
        {
            pause.Reset();
        }

        public void ResumeLoading()
        {
            pause.Set();
        }

        protected async Task<LoadMoreItemsResult> LoadMoreItemsAsync(CancellationToken c, uint count)
        {
            try
            {
                if (!HasMoreItems) return new LoadMoreItemsResult() { Count = 0 };
                LoadMoreItemsResult toret = new LoadMoreItemsResult() { Count = 0 };
                JsonObject searchres = null;
                try
                {
                    if (nexturl == "begin")
                        searchres = await new PixivCS
                            .PixivAppAPI(OverAll.GlobalBaseAPI)
                            .SearchIllust(word, searchTarget, sort, duration);
                    else
                    {
                        Uri next = new Uri(nexturl);
                        string getparam(string param) => HttpUtility.ParseQueryString(next.Query).Get(param);
                        var test = getparam("duration");
                        searchres = await new PixivCS
                            .PixivAppAPI(OverAll.GlobalBaseAPI)
                            .SearchIllust(getparam("word"), getparam("search_target"),
                            getparam("sort"), getparam("duration"), getparam("filter"), getparam("offset"));
                    }
                }
                catch
                {
                    return toret;
                }
                nexturl = searchres["next_url"].TryGetString();
                foreach (var recillust in searchres["illusts"].GetArray())
                {
                    await Task.Run(() => pause.WaitOne());
                    if (_emergencyStop)
                    {
                        nexturl = "";
                        Clear();
                        return new LoadMoreItemsResult() { Count = 0 };
                    }
                    WaterfallItem recommendi = WaterfallItem.FromJsonValue(recillust.GetObject());
                    var recommendmodel = ViewModels.WaterfallItemViewModel.FromItem(recommendi);
                    await recommendmodel.LoadImageAsync();
                    Add(recommendmodel);
                    toret.Count++;
                }
                return toret;
            }
            finally
            {
                _busy = false;
                if (_emergencyStop)
                {
                    nexturl = "";
                    Clear();
                    GC.Collect();
                }
            }
        }
    }
}


//------------------------------------------------------------------------------
// <copyright company="LeanKit Inc.">
//     Copyright (c) LeanKit Inc.  All rights reserved.
// </copyright> 
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using IntegrationService.Targets.GitHub;
using IntegrationService.Util;
using LeanKit.API.Client.Library;
using LeanKit.API.Client.Library.TransferObjects;
using Moq;
using NUnit.Framework;
using RestSharp;
using ServiceStack.Text;
using Should;
using Ploeh.SemanticComparison.Fluent;

namespace IntegrationService.Tests.GitHub.Pulls
{
    public class GitHubPullsSpec : IntegrationBaseSpec
    {
		protected Mock<IRestClient> MockRestClient;
		protected IRestClient RestClient;
	    protected new GitHubPulls TestItem;

	    protected override void OnCreateMockObjects()
	    {
			base.OnCreateMockObjects();
		    MockRestClient = new Mock<IRestClient>();
			RestClient = MockRestClient.Object;
	    }

	    protected override void OnArrange()
        {
            MockConfigurationProvider.Setup(x => x.GetConfiguration()).Returns(TestConfig);
            MockLeanKitClientFactory.Setup(x => x.Create(It.IsAny<LeanKitAccountAuth>())).Returns(LeanKitApi);
		    MockRestClient.Setup(x => x.Execute(It.IsAny<IRestRequest>())).Returns((IRestResponse)null);
        }

        protected override void OnStartTest()
        {
            TestItem = new GitHubPulls(SubscriptionManager, ConfigurationProvider, LocalStorage, LeanKitClientFactory, RestClient);
        }

    }

    [TestFixture]
    public class When_starting_with_a_valid_configuration : GitHubPullsSpec
    {
        protected override void OnStartFixture()
        {
            TestConfig = Test<Configuration>.Item;
        }

        protected override void OnArrange()
        {
            base.OnArrange();
            MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<int>())).Returns(new Board());
        }

        [Test]
        public void It_should_get_board_for_each_mapping()
        {
            MockLeanKitApi.Verify(x=>x.GetBoard(It.IsAny<long>()),Times.Exactly(TestConfig.Mappings.Count));
        }
    }

    [TestFixture]
    public class When_starting_with_an_invalid_configuration : GitHubPullsSpec
    {
        protected override void OnArrange()
        {
            MockConfigurationProvider.Setup(x => x.GetConfiguration()).Throws<ConfigurationErrorsException>();
        }
        
        [Test]
        public void It_should_not_attempt_to_load_app_settings()
        {
            MockLocalStorage.Verify(x=>x.Load(), Times.Never());
        }

        [Test]
        public void It_should_not_attempt_to_connect_to_leankit()
        {
            MockLeanKitClientFactory.Verify(x => x.Create(It.IsAny<LeanKitAccountAuth>()), Times.Never());
        }
    }

    [TestFixture]
    public class When_starting_with_valid_leankit_acount : GitHubPullsSpec
    {
        private LeanKitAccountAuth TestAuth;
        protected override void OnStartFixture()
        {
            TestConfig = Test<Configuration>.Item;
            TestAuth = new LeanKitAccountAuth
            {
                Hostname = TestConfig.LeanKit.Url,
				UrlTemplateOverride = "http://{0}.leankit.com",
                Username = TestConfig.LeanKit.User,
                Password = TestConfig.LeanKit.Password
            };

        }

        [Test]
        public void It_should_use_configured_credentials_for_leankit()
        {
            var likeness = TestAuth.AsSource().OfLikeness<LeanKitAccountAuth>();
            MockLeanKitClientFactory.Verify(x => x.Create(It.Is<LeanKitAccountAuth>(auth => likeness.Equals(auth))));
        }
    }

	[TestFixture]
	public class When_calculating_priority : GitHubPullsSpec
	{
		protected override void OnStartFixture() 
		{
			TestConfig = Test<Configuration>.Item;
		}

		[Test]
		public void It_should_default_to_normal()
		{
			GithubConversionExtensions.CalculateLeanKitPriority((GitHubPulls.Pull)null).ShouldEqual(1);
			GithubConversionExtensions.CalculateLeanKitPriority(new GitHubPulls.Pull()).ShouldEqual(1);
		}
	}

	[TestFixture]
	public class When_calculating_card_type : GitHubPullsSpec
	{
		private Board _testBoard;
		private BoardMapping _mapping;

		protected override void OnStartFixture() 
		{
			_testBoard = Test<Board>.Item;
			foreach (var cardType in _testBoard.CardTypes)
				cardType.IsDefault = false;
			_testBoard.CardTypes.Last().IsDefault = true;
			_mapping = Test<BoardMapping>.Item;
			_mapping.Identity.LeanKit = _testBoard.Id;
			TestConfig = Test<Configuration>.Item;
			TestConfig.Mappings = new List<BoardMapping> { _mapping };
		}

		protected override void OnArrange() 
		{
			base.OnArrange();
			MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<long>())).Returns(_testBoard);
		}

		[Test]
		public void It_should_always_return_default_card_type_for_board()
		{
			GithubConversionExtensions.CalculateLeanKitCardType(_mapping, new GitHubPulls.Pull()).Id.ShouldEqual(_testBoard.CardTypes.Last().Id);
		}

	}

	[TestFixture]
	public class When_calculating_assigned_user : GitHubPullsSpec 
	{
		private Board _testBoard;
		private BoardMapping _mapping;

		protected override void OnStartFixture() 
		{
			_testBoard = Test<Board>.Item;
			int ctr = 0;
			foreach (var boardUser in _testBoard.BoardUsers) 
			{
				if (ctr == 0) 
				{
					boardUser.UserName = "jcash";
					boardUser.FullName = "Johnny Cash";
					boardUser.EmailAddress = "johnny@cash.com";
					boardUser.Id = 101;
				}
				ctr++;
			}
			_mapping = Test<BoardMapping>.Item;
			_mapping.Identity.LeanKit = _testBoard.Id;
			TestConfig = Test<Configuration>.Item;
			TestConfig.Mappings = new List<BoardMapping> { _mapping };
		}

		protected override void OnArrange() 
		{
			base.OnArrange();
			MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<long>())).Returns(_testBoard);
		}

		[Test]
		public void It_should_return_userid_on_matched_username() 
		{
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "JCash" } }
				}, LeanKitApi).ShouldEqual(101);
		}

		[Test]
		public void It_should_return_null_on_nonmatched_username() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "willyb" } }
				}, LeanKitApi).ShouldBeNull();
		}

		[Test]
		public void It_should_return_null_on_empty_username() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "" } }
				}, LeanKitApi).ShouldBeNull();
		}


		[Test]
		public void It_should_return_userid_on_matched_email() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "Johnny@Cash.com" } }
				}, LeanKitApi).ShouldEqual(101);
		}

		[Test]
		public void It_should_return_null_on_nonmatched_email() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "willyB@Cash.com" } }
				}, LeanKitApi).ShouldBeNull();
		}

		[Test]
		public void It_should_return_userid_on_matched_fullname() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "Johnny Cash" } }
				}, LeanKitApi).ShouldEqual(101);
		}

		[Test]
		public void It_should_return_null_on_nonmatched_fullname() {
			GithubConversionExtensions.CalculateLeanKitAssignedUserId(_mapping.Identity.LeanKit,
				new GitHubPulls.Pull()
				{
					Base = new GitHubPulls.Branch() { User = new GitHubPulls.User() { Login = "Willy Cash" } }
				}, LeanKitApi).ShouldBeNull();
		}

	}

	public class When_updating_properties_of_target_item : GitHubPullsSpec 
	{
		private Board _testBoard;
		private BoardMapping _mapping;

		public class TestGitHubPulls : GitHubPulls 
		{
			public TestGitHubPulls(IBoardSubscriptionManager subscriptions,
								IConfigurationProvider<Configuration> configurationProvider,
								ILocalStorage<AppSettings> localStorage,
								ILeanKitClientFactory leanKitClientFactory,
								IRestClient restClient)
				: base(subscriptions, configurationProvider, localStorage, leanKitClientFactory, restClient) { }

			public void TestCardUpdated(Card card, List<string> updatedItems, BoardMapping boardMapping) 
			{
				base.CardUpdated(card, updatedItems, boardMapping);
			}
		}

		protected override void OnStartFixture() 
		{
			_testBoard = Test<Board>.Item;
			_mapping = Test<BoardMapping>.Item;
			_mapping.Identity.LeanKit = _testBoard.Id;
			_mapping.LaneToStatesMap.Add(1, new List<string> { "open" });
			_mapping.LaneToStatesMap.Add(2, new List<string> { "closed" });
			TestConfig = Test<Configuration>.Item;
			TestConfig.Mappings = new List<BoardMapping> { _mapping };
		}

		protected override void OnArrange() 
		{
			base.OnArrange();

			MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<long>())).Returns(_testBoard);

			var serializer = new JsonSerializer<GitHubPulls.Pull>();

			var pull1 = new GitHubPulls.Pull()
			{
				Id = 1,
				Number = 1,
				Title = "Pull 1",
				Body = "Pull 1",
				State = "Open"
			};

			var restResponse1 = new RestResponse() { Content = serializer.SerializeToString(pull1), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.GET))).Returns(restResponse1);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.PATCH))).Returns(restResponse1);

			var pull2 = new GitHubPulls.Pull()
			{
				Id = 2,
				Number = 2,
				Title = "Pull 2",
				Body = "Pull 2",
				State = "Open"
			};

			var restResponse2 = new RestResponse() { Content = serializer.SerializeToString(pull2), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.GET))).Returns(restResponse2);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.PATCH))).Returns(restResponse2);

			var pull3 = new GitHubPulls.Pull()
			{
				Id = 3,
				Number = 3,
				Title = "Pull 3",
				Body = "Pull 3",
				State = "Open"
			};

			var restResponse3 = new RestResponse() { Content = serializer.SerializeToString(pull3), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.GET))).Returns(restResponse3);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.PATCH))).Returns(restResponse3);

			var pull4 = new GitHubPulls.Pull()
			{
				Id = 4,
				Number = 4,
				Title = "Pull 4",
				Body = "Pull 4",
				State = "Open"
			};

			var restResponse4 = new RestResponse() { Content = serializer.SerializeToString(pull4), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.GET))).Returns(restResponse4);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.PATCH))).Returns(restResponse4);
		}

		protected override void OnStartTest() 
		{
			TestItem = new TestGitHubPulls(SubscriptionManager, ConfigurationProvider, LocalStorage, LeanKitClientFactory, RestClient);
		}

		[Test]
		public void It_should_call_github_to_update_pull_if_many_properties_change() 
		{
			Card card = new Card();
			card.ExternalCardID = "1|1";
			card.ExternalSystemName = "GitHub";
			card.Description = "Pull 1 Description";
			card.Title = "Pull 1 Title";

			((TestGitHubPulls)TestItem).TestCardUpdated(card, new List<string>() { "Title", "Description" }, _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.PATCH)), Times.Exactly(1));
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_properties_do_not_change() 
		{
			Card card = new Card();
			card.ExternalCardID = "2|2";
			card.ExternalSystemName = "GitHub";
			card.Description = "Pull 2";
			card.Title = "Pull 2";

			((TestGitHubPulls)TestItem).TestCardUpdated(card, new List<string>() { "Title", "Description" }, _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.PATCH)), Times.Never());
		}

		[Test]
		public void It_should_call_github_to_update_pull_if_one_property_changes() 
		{
			Card card = new Card();
			card.ExternalCardID = "3|3";
			card.ExternalSystemName = "GitHub";
			card.Description = "Pull 3";
			card.Title = "Pull 3 Title";

			((TestGitHubPulls)TestItem).TestCardUpdated(card, new List<string>() { "Title" }, _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.PATCH)), Times.Exactly(1));
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_no_identified_properties_change() 
		{
			Card card = new Card();
			card.ExternalCardID = "4|4";
			card.ExternalSystemName = "GitHub";
			card.Description = "Pull 4";
			card.Title = "Pull 4";

			((TestGitHubPulls)TestItem).TestCardUpdated(card, new List<string>(), _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.PATCH)), Times.Never());
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_externalsystemname_is_different() 
		{
			Card card = new Card();
			card.ExternalCardID = "5|5";
			card.ExternalSystemName = "GitHubby";
			card.Description = "Pull 5";
			card.Title = "Pull 5";

			((TestGitHubPulls)TestItem).TestCardUpdated(card, new List<string>(), _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/5") && y.Method == Method.GET)), Times.Never());
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/5") && y.Method == Method.PATCH)), Times.Never());
		}
	}

	public class When_updating_state_of_target_item : GitHubPullsSpec 
	{
		private Board _testBoard;
		private BoardMapping _mapping;

		public class TestGitHubPulls : GitHubPulls 
		{
			public TestGitHubPulls(IBoardSubscriptionManager subscriptions,
								IConfigurationProvider<Configuration> configurationProvider,
								ILocalStorage<AppSettings> localStorage,
								ILeanKitClientFactory leanKitClientFactory,
								IRestClient restClient)
				: base(subscriptions, configurationProvider, localStorage, leanKitClientFactory, restClient) { }

			public void TestUpdateStateOfExternalItem(Card card, List<string> laneStateMap, BoardMapping boardConfig) 
			{
				base.UpdateStateOfExternalItem(card, laneStateMap, boardConfig, true);
			}
		}

		protected override void OnStartFixture() 
		{
			_testBoard = Test<Board>.Item;
			_mapping = Test<BoardMapping>.Item;
			_mapping.Identity.LeanKit = _testBoard.Id;
			_mapping.LaneToStatesMap.Add(1, new List<string> { "open" });
			_mapping.LaneToStatesMap.Add(2, new List<string> { "closed" });
			TestConfig = Test<Configuration>.Item;
			TestConfig.Mappings = new List<BoardMapping> { _mapping };
		}

		protected override void OnArrange() 
		{
			base.OnArrange();

			MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<long>())).Returns(_testBoard);

			var serializer = new JsonSerializer<GitHubPulls.Pull>();

			var pull1 = new GitHubPulls.Pull()
			{
				Id = 1,
				Number = 1,
				State = "Open"
			};

			var restResponse1 = new RestResponse() { Content = serializer.SerializeToString(pull1), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.GET))).Returns(restResponse1);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.PATCH))).Returns(restResponse1);

			var pull2 = new GitHubPulls.Pull()
			{
				Id = 2,
				Number = 2,
				State = "Closed"
			};

			var restResponse2 = new RestResponse() { Content = serializer.SerializeToString(pull2), StatusCode = HttpStatusCode.OK };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.GET))).Returns(restResponse2);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.PATCH))).Returns(restResponse2);

			var errorSerializer = new JsonSerializer<GitHubPulls.ErrorMessage>();
			var errorResponse = new RestResponse() { Content = errorSerializer.SerializeToString(new GitHubPulls.ErrorMessage() { Message = "Error" }), StatusCode = HttpStatusCode.NotFound };

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.GET))).Returns(errorResponse);
			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.PATCH))).Returns(errorResponse);
		}

		protected override void OnStartTest() 
		{
			TestItem = new TestGitHubPulls(SubscriptionManager, ConfigurationProvider, LocalStorage, LeanKitClientFactory, RestClient);
		}

		[Test]
		public void It_should_call_github_to_update_pull_if_pull_state_is_not_end_state() 
		{
			Card card = new Card() { Id = 1, ExternalSystemName = "GitHub", ExternalCardID = "1|1"};
			((TestGitHubPulls)TestItem).TestUpdateStateOfExternalItem(card, _mapping.LaneToStatesMap[2], _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/1") && y.Method == Method.PATCH)), Times.Exactly(1));
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_state_is_already_end_state() 
		{
			Card card = new Card() { Id = 2, ExternalSystemName = "GitHub", ExternalCardID = "2|2" };
			((TestGitHubPulls)TestItem).TestUpdateStateOfExternalItem(card, _mapping.LaneToStatesMap[2], _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.PATCH)), Times.Never());
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_card_does_not_have_external_id() 
		{
			Card card = new Card() { Id = 2, ExternalSystemName = "GitHub", ExternalCardID = "" };
			((TestGitHubPulls)TestItem).TestUpdateStateOfExternalItem(card, _mapping.LaneToStatesMap[2], _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.GET)), Times.Never());
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/2") && y.Method == Method.PATCH)), Times.Never());
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_github_does_not_have_matching_issue() 
		{
			Card card = new Card() { Id = 3, ExternalSystemName = "GitHub", ExternalCardID = "3|3" };
			((TestGitHubPulls)TestItem).TestUpdateStateOfExternalItem(card, _mapping.LaneToStatesMap[2], _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.GET)), Times.Exactly(1));
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/3") && y.Method == Method.PATCH)), Times.Never());
		}

		[Test]
		public void It_should_not_call_github_to_update_pull_if_externalsystemname_does_not_match() 
		{
			Card card = new Card() { Id = 4, ExternalSystemName = "GitHubby", ExternalCardID = "4|4" };
			((TestGitHubPulls)TestItem).TestUpdateStateOfExternalItem(card, _mapping.LaneToStatesMap[2], _mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.GET)), Times.Never());
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("pulls/4") && y.Method == Method.PATCH)), Times.Never());
		}
	}

	public class When_syncronizing_with_target_system : GitHubPullsSpec 
	{
		private Board _testBoard;
		private BoardMapping _mapping;
		private CardAddResult _testCardAddResult1;

		public class TestGitHubPulls : GitHubPulls 
		{
			public TestGitHubPulls(IBoardSubscriptionManager subscriptions,
									IConfigurationProvider<Configuration> configurationProvider,
									ILocalStorage<AppSettings> localStorage,
									ILeanKitClientFactory leanKitClientFactory,
									IRestClient restClient)
				: base(subscriptions, configurationProvider, localStorage, leanKitClientFactory, restClient) 
			{
				QueryDate = DateTime.UtcNow.AddMinutes(-1);
			}

			public void Syncronize(BoardMapping boardConfig) 
			{
				base.Synchronize(boardConfig);
			}
		}

		protected override void OnStartFixture() 
		{
			_testBoard = Test<Board>.Item;
			_mapping = Test<BoardMapping>.Item;
			_testCardAddResult1 = Test<CardAddResult>.Item;
			_testCardAddResult1.CardId = 1;
			_mapping.Identity.LeanKit = _testBoard.Id;
			_mapping.LaneToStatesMap.Add(1, new List<string> { "open" });
			TestConfig = Test<Configuration>.Item;
			TestConfig.PollingFrequency = 5000;
			TestConfig.Mappings = new List<BoardMapping> { _mapping };
		}

		protected override void OnArrange() 
		{
			base.OnArrange();

			MockLeanKitApi.Setup(x => x.GetBoard(It.IsAny<long>())).Returns(_testBoard);

			var serializer = new JsonSerializer<List<GitHubPulls.Pull>>();

			var pull1 = new GitHubPulls.Pull()
			{
				Id = 1,
				Number = 1,
				State = "Open",
				Body = "New Pull 1",
				Title = "New Pull 1"
			};

			var pull2 = new GitHubPulls.Pull()
			{
				Id = 2,
				Number = 2,
				State = "Open",
				Body = "New Pull 2",
				Title = "New Pull 2"
			};

			var pull3 = new GitHubPulls.Pull()
			{
				Id = 3,
				Number = 3,
				State = "Open",
				Body = "New Pull 3",
				Title = "New Pull 3"
			};

			var restResponse1 = new RestResponse()
			{
				Content = serializer.SerializeToString(new List<GitHubPulls.Pull>() { pull1 }),
				StatusCode = HttpStatusCode.OK
			};

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/1/pulls") && y.Method == Method.GET))).Returns(restResponse1);
			MockLeanKitApi.Setup(x => x.GetCardByExternalId(1, It.IsAny<string>())).Returns((Card)null);
			MockLeanKitApi.Setup(x => x.AddCard(1, It.IsAny<Card>(), It.IsAny<string>())).Returns(_testCardAddResult1);

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/2/pulls") && y.Method == Method.GET))).Returns(restResponse1);
			MockLeanKitApi.Setup(x => x.GetCardByExternalId(2, It.IsAny<string>())).Returns((Card)null);
			MockLeanKitApi.Setup(x => x.AddCard(2, It.IsAny<Card>(), It.IsAny<string>())).Returns(_testCardAddResult1);

			var restResponse3 = new RestResponse()
			{
				Content = serializer.SerializeToString(new List<GitHubPulls.Pull>() { pull1, pull2, pull3 }),
				StatusCode = HttpStatusCode.OK
			};

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/3/pulls") && y.Method == Method.GET))).Returns(restResponse3);
			MockLeanKitApi.Setup(x => x.GetCardByExternalId(3, It.IsAny<string>())).Returns((Card)null);
			MockLeanKitApi.Setup(x => x.AddCard(3, It.IsAny<Card>(), It.IsAny<string>())).Returns(_testCardAddResult1);

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/4/pulls") && y.Method == Method.GET))).Returns(restResponse1);
			MockLeanKitApi.Setup(x => x.GetCardByExternalId(4, It.IsAny<string>())).Returns(new Card() { Id = 4, ExternalSystemName = "GitHub"});
			MockLeanKitApi.Setup(x => x.AddCard(4, It.IsAny<Card>(), It.IsAny<string>())).Returns(_testCardAddResult1);

			MockRestClient.Setup(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/5/pulls") && y.Method == Method.GET))).Returns(restResponse1);
			MockLeanKitApi.Setup(x => x.GetCardByExternalId(5, It.IsAny<string>())).Returns(new Card() { Id = 5, ExternalSystemName = "GitHubber" });
			MockLeanKitApi.Setup(x => x.AddCard(5, It.IsAny<Card>(), It.IsAny<string>())).Returns(_testCardAddResult1);
		}

		protected override void OnStartTest() 
		{
			TestItem = new TestGitHubPulls(
				SubscriptionManager,
				ConfigurationProvider,
				LocalStorage,
				LeanKitClientFactory,
				RestClient);
		}

		[Test]
		public void It_should_call_github_to_get_list_of_pullrequests() 
		{
			_mapping.Identity.LeanKit = 1;
			_mapping.Identity.Target = "1";
			((TestGitHubPulls)TestItem).Syncronize(_mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/1/pulls") && y.Method == Method.GET)), Times.Exactly(1));
			MockLeanKitApi.Verify(x => x.AddCard(1, It.IsAny<Card>(), It.IsAny<string>()), Times.Exactly(1));
		}

		[Test]
		public void It_should_call_leankit_once_to_create_card_if_there_is_one_pullrequest() 
		{
			_mapping.Identity.LeanKit = 2;
			_mapping.Identity.Target = "2";
			((TestGitHubPulls)TestItem).Syncronize(_mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/2/pulls") && y.Method == Method.GET)), Times.Exactly(1));
			MockLeanKitApi.Verify(x => x.AddCard(2, It.IsAny<Card>(), It.IsAny<string>()), Times.Exactly(1));
		}

		[Test]
		public void It_should_call_leankit_multiple_times_to_create_card_if_there_are_multiple_pullrequests() 
		{
			_mapping.Identity.LeanKit = 3;
			_mapping.Identity.Target = "3";
			((TestGitHubPulls)TestItem).Syncronize(_mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/3/pulls") && y.Method == Method.GET)), Times.Exactly(1));
			MockLeanKitApi.Verify(x => x.AddCard(3, It.IsAny<Card>(), It.IsAny<string>()), Times.Exactly(3));
		}

		[Test]
		public void It_should_not_call_leankit_to_create_card_if_card_with_externalid_already_exists() 
		{
			_mapping.Identity.LeanKit = 4;
			_mapping.Identity.Target = "4";
			((TestGitHubPulls)TestItem).Syncronize(_mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/4/pulls") && y.Method == Method.GET)), Times.Exactly(1));
			MockLeanKitApi.Verify(x => x.AddCard(4, It.IsAny<Card>(), It.IsAny<string>()), Times.Never());
		}

		[Test]
		public void It_should_call_leankit_to_create_card_if_existing_card_has_externalid_but_externalsystemname_does_not_match() 
		{
			_mapping.Identity.LeanKit = 5;
			_mapping.Identity.Target = "5";
			((TestGitHubPulls)TestItem).Syncronize(_mapping);
			MockRestClient.Verify(x => x.Execute(It.Is<RestRequest>(y => y.Resource.Contains("/5/pulls") && y.Method == Method.GET)), Times.Exactly(1));
			MockLeanKitApi.Verify(x => x.AddCard(5, It.IsAny<Card>(), It.IsAny<string>()), Times.Exactly(1));
		}
	}
}
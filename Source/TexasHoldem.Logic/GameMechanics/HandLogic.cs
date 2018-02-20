﻿namespace TexasHoldem.Logic.GameMechanics
{
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Helpers;
    using TexasHoldem.Logic.Players;

    internal class HandLogic : IHandLogic
    {
        private readonly int handNumber;

        private readonly int smallBlind;

        private readonly IList<InternalPlayer> players;

        private readonly Deck deck;

        private readonly List<Card> communityCards;

        private readonly BettingLogic bettingLogic;

        private Dictionary<string, ICollection<Card>> showdownCards;

        public HandLogic(IList<InternalPlayer> players, int handNumber, int smallBlind)
        {
            this.handNumber = handNumber;
            this.smallBlind = smallBlind;
            this.players = players;
            this.deck = new Deck();
            this.communityCards = new List<Card>(5);
            this.bettingLogic = new BettingLogic(this.players, smallBlind);
            this.showdownCards = new Dictionary<string, ICollection<Card>>();
        }

        public void Play()
        {
            // Start the hand and deal cards to each player
            foreach (var player in this.players)
            {
                var startHandContext = new StartHandContext(
                    this.deck.GetNextCard(),
                    this.deck.GetNextCard(),
                    this.handNumber,
                    player.PlayerMoney.Money,
                    this.smallBlind,
                    this.players[0].Name);
                player.StartHand(startHandContext);
            }

            // Pre-flop -> blinds -> betting
            this.PlayRound(GameRoundType.PreFlop, 0);

            // Flop -> 3 cards -> betting
            if (this.players.Count(x => x.PlayerMoney.InHand) > 1)
            {
                this.PlayRound(GameRoundType.Flop, 3);
            }

            // Turn -> 1 card -> betting
            if (this.players.Count(x => x.PlayerMoney.InHand) > 1)
            {
                this.PlayRound(GameRoundType.Turn, 1);
            }

            // River -> 1 card -> betting
            if (this.players.Count(x => x.PlayerMoney.InHand) > 1)
            {
                this.PlayRound(GameRoundType.River, 1);
            }

            this.DetermineWinnerAndAddPot(this.bettingLogic.Pot, this.bettingLogic.MainPot, this.bettingLogic.SidePots);

            foreach (var player in this.players)
            {
                player.EndHand(new EndHandContext(this.showdownCards));
            }
        }

        private void DetermineWinnerAndAddPot(int pot, Pot mainPot, List<Pot> sidePot)
        {
            if (this.players.Count(x => x.PlayerMoney.InHand) == 1)
            {
                var winner = this.players.FirstOrDefault(x => x.PlayerMoney.InHand);
                winner.PlayerMoney.Money += pot;
            }
            else
            {
                // showdown
                foreach (var player in this.players)
                {
                    if (player.PlayerMoney.InHand)
                    {
                        this.showdownCards.Add(player.Name, player.Cards);
                    }
                }

                if (this.players.Count == 2)
                {
                    var betterHand = Helpers.CompareCards(
                    this.players[0].Cards.Concat(this.communityCards),
                    this.players[1].Cards.Concat(this.communityCards));
                    if (betterHand > 0)
                    {
                        this.players[0].PlayerMoney.Money += pot;
                    }
                    else if (betterHand < 0)
                    {
                        this.players[1].PlayerMoney.Money += pot;
                    }
                    else
                    {
                        this.players[0].PlayerMoney.Money += pot / 2;
                        this.players[1].PlayerMoney.Money += pot / 2;
                    }
                }
                else
                {
                    // Bubble sort
                    var playersInHand = this.players.Where(p => p.PlayerMoney.InHand).ToArray();
                    for (int element = 0; element < playersInHand.Length - 1; element++)
                    {
                        for (int i = 0; i < playersInHand.Length - (element + 1); i++)
                        {
                            if (Helpers.CompareCards(
                                playersInHand[i].Cards.Concat(this.communityCards),
                                playersInHand[i + 1].Cards.Concat(this.communityCards)) > 0)
                            {
                                var temp = playersInHand[i + 1];
                                playersInHand[i + 1] = playersInHand[i];
                                playersInHand[i] = temp;
                            }
                        }
                    }

                    var sortedByHandStrength = new Stack<List<InternalPlayer>>();
                    sortedByHandStrength.Push(new List<InternalPlayer> { playersInHand[0] });
                    for (int i = 0; i < playersInHand.Count() - 1; i++)
                    {
                        var betterHand = Helpers.CompareCards(
                            playersInHand[i].Cards.Concat(this.communityCards),
                            playersInHand[i + 1].Cards.Concat(this.communityCards));

                        if (betterHand < 0)
                        {
                            sortedByHandStrength.Push(new List<InternalPlayer> { playersInHand[i + 1] });
                        }
                        else if (betterHand == 0)
                        {
                            sortedByHandStrength.Peek().Add(playersInHand[i + 1]);
                        }
                    }

                    var remainingPots = new List<Pot>(sidePot);
                    remainingPots.Add(mainPot);

                    do
                    {
                        var winners = sortedByHandStrength.Pop();

                        var unusedPots = new List<Pot>();

                        foreach (var oneOfThePots in remainingPots)
                        {
                            var applicantsForThePot = oneOfThePots.Participants.Intersect(winners.Select(s => s.Name));
                            var count = applicantsForThePot.Count();
                            if (count > 0)
                            {
                                var prize = oneOfThePots.AmountOfMoney / count;
                                foreach (var applicant in applicantsForThePot)
                                {
                                    winners.First(x => x.Name == applicant).PlayerMoney.Money += prize;
                                }
                            }
                            else
                            {
                                unusedPots.Add(oneOfThePots);
                            }
                        }

                        remainingPots = unusedPots;
                    }
                    while (remainingPots.Count > 0);
                }
            }
        }

        private void PlayRound(GameRoundType gameRoundType, int communityCardsCount)
        {
            for (var i = 0; i < communityCardsCount; i++)
            {
                this.communityCards.Add(this.deck.GetNextCard());
            }

            foreach (var player in this.players)
            {
                var startRoundContext = new StartRoundContext(
                    gameRoundType,
                    this.communityCards.AsReadOnly(),
                    player.PlayerMoney.Money,
                    this.bettingLogic.Pot);
                player.StartRound(startRoundContext);
            }

            this.bettingLogic.Bet(gameRoundType);

            foreach (var player in this.players)
            {
                var endRoundContext = new EndRoundContext(this.bettingLogic.RoundBets);
                player.EndRound(endRoundContext);
            }
        }
    }
}
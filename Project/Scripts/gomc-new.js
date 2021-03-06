﻿// Gomc-specific JS

document.onreadystatechange = function () {
    if (document.readyState === 'interactive') {
        $('.gomc-jumbotron').css('background-color', '#91D3EF');
        $('.gomc-jumbotron h1').css('background-color', '#91D3EF');
        $('.carousel-control').css({
            'background-color': '#91D3EF',
            'background-image': 'none'
        });

        doFetchGomcAnnouncements();
        $("#gomcHome").on('slid.bs.carousel', function () {
            var focusedCard = $(this).find('div .active');
            if (focusedCard.hasClass('cardA')) {
                $('.gomc-jumbotron').css('background-color', '#91D3EF');
                $('.gomc-jumbotron h1').css('background-color', '#91D3EF');
                $('.carousel-control').css('background-color', '#91D3EF');

            } else if (focusedCard.hasClass('cardB')) {
                $('.gomc-jumbotron').css('background-color', '#91D3EF');
                $('.gomc-jumbotron h1').css('background-color', '#91D3EF');
                $('.carousel-control').css('background-color', '#91D3EF');

            } else if (focusedCard.hasClass('cardC')) {
                $('.gomc-jumbotron').css('background-color', '#91D3EF');
                $('.gomc-jumbotron h1').css('background-color', '#91D3EF');
                $('.carousel-control').css('background-color', '#91D3EF');
            }
        });
    }
}

$('#btn').click(function () {
    if ($('#btn').children().hasClass('glyphicon-align-justify')) {
        $('#btn').children().removeClass('glyphicon-align-justify');
        $('#btn').children().addClass('glyphicon-remove');
        $('header').css('margin-top', '22.5em');
    }
    else {
        $('#btn').children().removeClass('glyphicon-remove');
        $('#btn').children().addClass('glyphicon-align-justify');
        $('header').css('margin-top', '6.5em');
    }
 
});
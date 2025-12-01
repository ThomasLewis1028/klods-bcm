# LIT - LEGO Inventory Tracker 

Welcome to the LEGO Inventory Tracker. It's a tool to track your LEGO sets, the bricks you need, the bricks you have, move inventory between sets, link to the official instructions, and inform you what pieces you needed to buy.

It's open source so if you want to fix my late-night, alcohol-fueled, sleep-deprived, "what if I did this" code, please do. I'll check PRs if there are any.

### Features

- Fully self-hosted. As much as I'd love to make money on a side project, I care a lot more about providing my talents to te open source community when possible.
- Easy to build and deploy with Docker Compose (thanks dad).
- Pulls data from Rebrickable on-demand, and then stores it locally so as to only use an API call as needed.
- Stores data in a postgres database which makes it easy to look at the data and fix things if needed.
- Track a personal inventory of loose bricks.
- Tracks the bill of materials for each set and allows you to denote what pieces you have for each set. If you need 8 technic axles but you only have 7, it will mark how many pieces you're missing.
- Allow the user to move stock from the loose inventory to the set stock.
- Allow a user to have more than one of the same set, each with their own inventories.
- When looking at a brick, you can see how many you need, how many you have, and which sets need that brick and how many it needs.
- Link directly to the instruction on LEGO's official instructions.
- It's neat (I am not biased).

### Why?

I started collecting LEGO when I was like 3-4 years old. I can distinctly remember coming out to our apartment living room one Christmas and seeing Santa left me Watto's Junkyard (7186), the Podracing Bucket (7159), and Anakin's Podracer (7131), fully asembled, ready to play.

Since then, between my brother and I, we've collected over a hundred LEGO sets, built most of them, tore apart all of them, lost hundreds of pieces, gathered random pieces left by friends, and left them all in a big tub with no organization.

In college, I went back to visit my parents during one of the breaks and I had the idea to try and rebuild all of my old sets. I ended up building a few of them but noticed some of the colors seemed off. I didn't care at first, until I realized that some time in the last 20 or so years, LEGO changed the color of grey. That meant I had to tear apart all the sets and start over, and I was too lazy, so I left them semi-organized in some tubs, and I didn't think about them again.

Over the next few years, my parents would gift me LEGO sets, I'd build them at their house, and then I'd go back to college. Eventually, I moved a few states away, brought a few of the sets I had built that I particularly enjoyed, and then every birthday, Christmas, and even Easter, thye'd send me another one.

At some point, I thought a bit about those old sets and I wanted to try again and rebuild them. In 2024, my wife and I drove 1100 miles for Christmas with my end goal being to bring every brick home, organize them, sort them into bags for sets, and then send my brother back his sets, and display mine.

I looked around and found a few websites that allowed you to track your sets, even track your bricks, but none of them did quite what I was wanting. Rebrickable is great, but it tracks the sets you have and allows you to add the bricks, but it didn't seem to have an intuitiive way to say "I have 4 of this brick, and I need 5 across two sets, so I'll put 3 on one and 1 on the other." Brickset was similar in that it works really well to track your sets but not to track the bricks you have and need. Nothing against either of these two sites, they're both great, but they didn't quite do what I wanted in the way I wanted.

So, my dad and I, having a bunch of free time while off work for the end of the year, started building this little thing, to fulfill my own persnal needs, even if no one else cared.

### How to build

Download the app and run the Docker Compose. Eventually I'll make it even easier.

### Future plans

- Upload the Docker image somewhere
- Add user authentication 
- Allow users to upload and manage their own API key (and encrypt it)
- Allow users to have and manage their own inventories 
- Allow users to transfer stock between their inventories
- Make a mobile app to connect to your instance 
- Maybe offer a subscription for me to host it for you (maybe maybe not idk)
- Make the UI more pretty and less stock MudBlazor-y
- Change the name maybe
- Allow substitution parts so if you want to build the Millennium Falcon with random colors, you could track that